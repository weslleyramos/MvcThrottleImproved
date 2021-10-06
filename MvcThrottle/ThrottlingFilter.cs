using System;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using MvcThrottleImproved.Enums;
using MvcThrottleImproved.Extensions;
using MvcThrottleImproved.IP;
using MvcThrottleImproved.Repositories;

namespace MvcThrottleImproved
{
    public class ThrottlingFilter : ActionFilterAttribute
    {
        private readonly ThrottlingCore _core;
        private readonly Language _language;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrottlingFilter"/> class.
        /// By default, the <see cref="QuotaExceededResponseCode"/> property 
        /// is set to 429 (Too Many Requests).
        /// </summary>
        public ThrottlingFilter()
        {
            QuotaExceededResponseCode = (HttpStatusCode)429;

            _language = Language.EN;
            _core = new ThrottlingCore
            {
                ThrottleRepository = new CacheRepository()
            };
        }

        /// <summary>
        /// Creates a new instance of the filter class.
        /// By default, the <see cref="QuotaExceededResponseCode"/> property 
        /// is set to 429 (Too Many Requests).
        /// </summary>
        public ThrottlingFilter(ThrottlePolicy policy,
            IThrottleRepository throttleRepository,
            IThrottleLogger logger,
            Language language = Language.EN,
            IIpAddressParser ipAddressParser = null)
        {
            Policy = policy;

            _core = new ThrottlingCore
            {
                ThrottleRepository = throttleRepository,
                Policy = policy,
                IpAddressParser = ipAddressParser ?? new IpAddressParser()
            };

            _language = language;
            Logger = logger;
            QuotaExceededResponseCode = (HttpStatusCode)429;
        }

        /// <summary>
        /// Throttling rate limits policy
        /// </summary>
        public ThrottlePolicy Policy { get; set; }

        /// <summary>
        /// Log blocked requests
        /// </summary>
        public IThrottleLogger Logger { get; set; }

        /// <summary>
        /// If none specifed the default will be: 
        /// HTTP request quota exceeded! maximum admitted {0} per {1}
        /// </summary>
        public string QuotaExceededMessage { get; set; }

        /// <summary>
        /// Gets or sets the value to return as the HTTP status 
        /// code when a request is rejected because of the
        /// throttling policy. The default value is 429 (Too Many Requests).
        /// </summary>
        public HttpStatusCode QuotaExceededResponseCode { get; set; }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var applyThrottling = ApplyThrottling(filterContext, out var attrPolicy);

            if (Policy != null && applyThrottling)
            {
                var identity = SetIdentity(filterContext.HttpContext.Request);

                if (!_core.IsWhitelisted(identity))
                {
                    var timeSpan = TimeSpan.FromSeconds(1);

                    var rates = Policy.Rates.AsEnumerable();
                    if (Policy.StackBlockedRequests)
                    {
                        //all requests including the rejected ones will stack in this order: day, hour, min, sec
                        //if a client hits the hour limit then the minutes and seconds counters will expire and will eventually get erased from cache
                        rates = Policy.Rates.Reverse();
                    }

                    //apply policy
                    //the IP rules are applied last and will overwrite any client rule you might defined
                    var suspendTime = attrPolicy.SuspendTime > 0 
                        ? attrPolicy.SuspendTime 
                        : Policy.SuspendTime;

                    if (rates != null)
                        foreach (var rate in rates)
                        {
                            var rateLimitPeriod = rate.Key;
                            var rateLimit = rate.Value;

                            switch (rateLimitPeriod)
                            {
                                case RateLimitPeriod.Second:
                                    timeSpan = TimeSpan.FromSeconds(1);
                                    break;
                                case RateLimitPeriod.Minute:
                                    timeSpan = TimeSpan.FromMinutes(1);
                                    break;
                                case RateLimitPeriod.Hour:
                                    timeSpan = TimeSpan.FromHours(1);
                                    break;
                                case RateLimitPeriod.Day:
                                    timeSpan = TimeSpan.FromDays(1);
                                    break;
                                case RateLimitPeriod.Week:
                                    timeSpan = TimeSpan.FromDays(7);
                                    break;
                            }

                            //increment counter
                            var throttleCounter = _core.ProcessRequest(identity, timeSpan, rateLimitPeriod, rateLimit,
                                suspendTime, out var requestId);

                            if (throttleCounter.TotalRequests >= rateLimit && suspendTime > 0)
                                timeSpan = _core.GetSuspendSpanFromPeriod(rateLimitPeriod, timeSpan, suspendTime);

                            if (throttleCounter.Timestamp + timeSpan < DateTime.UtcNow)
                                continue;

                            //apply EnableThrottlingAttribute policy
                            var attrLimit = attrPolicy.GetLimit(rateLimitPeriod);
                            if (attrLimit > 0)
                            {
                                rateLimit = attrLimit;
                            }

                            //apply endpoint rate limits
                            if (Policy.EndpointRules != null)
                            {
                                var rules = Policy.EndpointRules.Where(x =>
                                    identity.Endpoint.IndexOf(x.Key, 0, StringComparison.InvariantCultureIgnoreCase) !=
                                    -1).ToList();
                                if (rules.Any())
                                {
                                    //get the lower limit from all applying rules
                                    var customRate = (from r in rules
                                        let rateValue = r.Value.GetLimit(rateLimitPeriod)
                                        select rateValue).Min();

                                    if (customRate > 0)
                                    {
                                        rateLimit = customRate;
                                    }
                                }
                            }

                            //apply custom rate limit for clients that will override endpoint limits
                            if (Policy.ClientRules != null && Policy.ClientRules.Keys.Contains(identity.ClientKey))
                            {
                                var limit = Policy.ClientRules[identity.ClientKey].GetLimit(rateLimitPeriod);
                                if (limit > 0) rateLimit = limit;
                            }

                            //apply custom rate limit for user agent
                            if (Policy.UserAgentRules != null && !string.IsNullOrEmpty(identity.UserAgent))
                            {
                                var rules = Policy.UserAgentRules.Where(x =>
                                    identity.UserAgent.IndexOf(x.Key, 0, StringComparison.InvariantCultureIgnoreCase) !=
                                    -1).ToList();
                                if (rules.Any())
                                {
                                    //get the lower limit from all applying rules
                                    var customRate = (from r in rules
                                        let rateValue = r.Value.GetLimit(rateLimitPeriod)
                                        select rateValue).Min();
                                    rateLimit = customRate;
                                }
                            }

                            //enforce ip rate limit as is most specific 
                            if (Policy.IpRules != null && _core.IpAddressParser.ContainsIp(Policy.IpRules.Keys.ToList(),
                                    identity.ClientIp, out var ipRule))
                            {
                                var limit = Policy.IpRules[ipRule].GetLimit(rateLimitPeriod);
                                if (limit > 0) rateLimit = limit;
                            }

                            //check if limit is reached
                            if (rateLimit > 0 && throttleCounter.TotalRequests > rateLimit)
                            {
                                //log blocked request
                                Logger?.Log(ComputeLogEntry(requestId, identity, throttleCounter,
                                    rateLimitPeriod.ToString(), rateLimit, filterContext.HttpContext.Request));

                                //break execution and return 409 
                                var message = string.IsNullOrEmpty(QuotaExceededMessage)
                                    ? "HTTP request quota exceeded! maximum admitted {0} per {1}"
                                    : QuotaExceededMessage;

                                //add status code and retry after x seconds to response
                                filterContext.HttpContext.Response.StatusCode = (int) QuotaExceededResponseCode;
                                filterContext.HttpContext.Response.Headers.Set("Retry-After",
                                    _core.RetryAfterFrom(throttleCounter.Timestamp, rateLimitPeriod));

                                filterContext.Result = QuotaExceededResult(
                                    filterContext.RequestContext,
                                    string.Format(message, rateLimit, rateLimitPeriod.ToLang(_language)),
                                    QuotaExceededResponseCode,
                                    requestId);

                                return;
                            }
                        }
                }
            }

            base.OnActionExecuting(filterContext);
        }

        protected virtual RequestIdentity SetIdentity(HttpRequestBase request)
        {
            var entry = new RequestIdentity
            {
                ClientIp = _core.GetClientIp(request),
                ClientKey = request.IsAuthenticated ? "auth" : "anon"
            };

            var rd = request.RequestContext.RouteData;
            var currentAction = rd.GetRequiredString("action");
            var currentController = rd.GetRequiredString("controller");

            switch (Policy.EndpointType)
            {
                case EndpointThrottlingType.PathAndQuery:
                    entry.Endpoint = request.Url?.PathAndQuery;
                    break;
                case EndpointThrottlingType.ControllerAndAction:
                    entry.Endpoint = currentController + "/" + currentAction;
                    break;
                case EndpointThrottlingType.Controller:
                    entry.Endpoint = currentController;
                    break;
                default:
                    entry.Endpoint = request.Url?.AbsolutePath;
                    break;
            }

            //case insensitive routes
            entry.Endpoint = entry.Endpoint?.ToLowerInvariant();

            entry.UserAgent = request.UserAgent;

            return entry;
        }

        private bool ApplyThrottling(ActionExecutingContext filterContext, out EnableThrottlingAttribute attr)
        {
            var applyThrottling = false;
            attr = null;

            if (filterContext.ActionDescriptor.ControllerDescriptor.IsDefined(typeof(EnableThrottlingAttribute), true))
            {
                attr = (EnableThrottlingAttribute)filterContext.ActionDescriptor.ControllerDescriptor.GetCustomAttributes(typeof(EnableThrottlingAttribute), true).First();
                applyThrottling = true;
            }

            //disabled on the class
            if (filterContext.ActionDescriptor.ControllerDescriptor.IsDefined(typeof(DisableThrottlingAttribute), true))
            {
                applyThrottling = false;
            }

            if (filterContext.ActionDescriptor.IsDefined(typeof(EnableThrottlingAttribute), true))
            {
                attr = (EnableThrottlingAttribute)filterContext.ActionDescriptor.GetCustomAttributes(typeof(EnableThrottlingAttribute), true).First();
                applyThrottling = true;
            }

            //explicit disabled
            if (filterContext.ActionDescriptor.IsDefined(typeof(DisableThrottlingAttribute), true))
            {
                applyThrottling = false;
            }

            return applyThrottling;
        }

        protected virtual ActionResult QuotaExceededResult(RequestContext filterContext, string message, HttpStatusCode responseCode, string requestId)
        {
            return new HttpStatusCodeResult(responseCode, message);
        }

        private ThrottleLogEntry ComputeLogEntry(string requestId, RequestIdentity identity, ThrottleCounter throttleCounter, string rateLimitPeriod, long rateLimit, HttpRequestBase request)
        {
            return new ThrottleLogEntry
            {
                ClientIp = identity.ClientIp,
                ClientKey = identity.ClientKey,
                Endpoint = identity.Endpoint,
                UserAgent = identity.UserAgent,
                LogDate = DateTime.UtcNow,
                RateLimit = rateLimit,
                RateLimitPeriod = rateLimitPeriod,
                RequestId = requestId,
                StartPeriod = throttleCounter.Timestamp,
                TotalRequests = throttleCounter.TotalRequests,
                Request = request
            };
        }
    }
}