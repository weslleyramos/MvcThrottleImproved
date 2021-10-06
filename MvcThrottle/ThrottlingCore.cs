using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using MvcThrottleImproved.IP;
using MvcThrottleImproved.Repositories;

namespace MvcThrottleImproved
{
    internal class ThrottlingCore
    {
        internal ThrottlePolicy Policy { get; set; }
        internal IThrottleRepository ThrottleRepository { get; set; }
        internal IIpAddressParser IpAddressParser { get; set; }

        private static readonly object ProcessLocker = new object();
        internal ThrottleCounter ProcessRequest(RequestIdentity requestIdentity, TimeSpan timeSpan, RateLimitPeriod period, long rateLimit, long suspendTime, out string id)
        {
            var throttleCounter = new ThrottleCounter()
            {
                Timestamp = DateTime.UtcNow,
                TotalRequests = 1
            };

            id = ComputeThrottleKey(requestIdentity, period);

            //serial reads and writes
            lock (ProcessLocker)
            {
                var entry = ThrottleRepository.FirstOrDefault(id);
                if (entry.HasValue)
                {
                    var timeStamp = entry.Value.Timestamp;
                    if (entry.Value.TotalRequests >= rateLimit && suspendTime > 0)
                        timeSpan = GetSuspendSpanFromPeriod(period, timeSpan, suspendTime);

                    //entry has not expired
                    if (entry.Value.Timestamp + timeSpan >= DateTime.UtcNow)
                    {
                        //increment request count
                        var totalRequests = entry.Value.TotalRequests + 1;

                        //deep copy
                        throttleCounter = new ThrottleCounter
                        {
                            Timestamp = timeStamp,
                            TotalRequests = totalRequests
                        };

                    }
                }

                //stores: id (string) - timestamp (datetime) - total (long)
                ThrottleRepository.Save(id, throttleCounter, timeSpan);
            }

            return throttleCounter;
        }

        internal TimeSpan GetSuspendSpanFromPeriod(RateLimitPeriod rateLimitPeriod, TimeSpan timeSpan, long suspendTime)
        {
            switch (rateLimitPeriod)
            {
                case RateLimitPeriod.Second:
                    timeSpan = (suspendTime > 1) ? TimeSpan.FromSeconds(suspendTime) : timeSpan;
                    break;
                case RateLimitPeriod.Minute:
                    timeSpan = (suspendTime > 60) ? TimeSpan.FromSeconds(suspendTime) : (timeSpan + TimeSpan.FromSeconds(suspendTime));
                    break;
                case RateLimitPeriod.Hour:
                    timeSpan += TimeSpan.FromSeconds(suspendTime);
                    break;
                case RateLimitPeriod.Day:
                    timeSpan += TimeSpan.FromSeconds(suspendTime);
                    break;
                case RateLimitPeriod.Week:
                    timeSpan += TimeSpan.FromSeconds(suspendTime);
                    break;
            }

            return timeSpan;
        }

        internal bool IsWhitelisted(RequestIdentity requestIdentity)
        {
            if (Policy.IpThrottling)
                if (Policy.IpWhitelist != null && IpAddressParser.ContainsIp(Policy.IpWhitelist, requestIdentity.ClientIp))
                    return true;

            if (Policy.ClientThrottling)
                if (Policy.ClientWhitelist != null && Policy.ClientWhitelist.Contains(requestIdentity.ClientKey))
                    return true;

            if (Policy.EndpointThrottling)
                if (Policy.EndpointWhitelist != null &&
                    Policy.EndpointWhitelist.Any(x => requestIdentity.Endpoint.IndexOf(x, 0, StringComparison.InvariantCultureIgnoreCase) != -1))
                    return true;

            if (Policy.UserAgentThrottling && requestIdentity.UserAgent != null)
                if (Policy.UserAgentWhitelist != null &&
                    Policy.UserAgentWhitelist.Any(x => requestIdentity.UserAgent.IndexOf(x, 0, StringComparison.InvariantCultureIgnoreCase) != -1))
                    return true;

            return false;
        }

        internal string RetryAfterFrom(DateTime timestamp, RateLimitPeriod period)
        {
            var secondsPast = Convert.ToInt32((DateTime.UtcNow - timestamp).TotalSeconds);
            var retryAfter = 1;
            switch (period)
            {
                case RateLimitPeriod.Minute:
                    retryAfter = 60;
                    break;
                case RateLimitPeriod.Hour:
                    retryAfter = 60 * 60;
                    break;
                case RateLimitPeriod.Day:
                    retryAfter = 60 * 60 * 24;
                    break;
                case RateLimitPeriod.Week:
                    retryAfter = 60 * 60 * 24 * 7;
                    break;
            }
            retryAfter = retryAfter > 1 ? retryAfter - secondsPast : 1;
            return retryAfter.ToString(CultureInfo.InvariantCulture);
        }

        internal string ComputeThrottleKey(RequestIdentity requestIdentity, RateLimitPeriod period)
        {
            var keyValues = new List<string>()
                {
                    "throttle"
                };

            if (Policy.IpThrottling)
                keyValues.Add(requestIdentity.ClientIp);

            if (Policy.ClientThrottling)
                keyValues.Add(requestIdentity.ClientKey);

            if (Policy.EndpointThrottling)
                keyValues.Add(requestIdentity.Endpoint);

            if (Policy.UserAgentThrottling)
                keyValues.Add(requestIdentity.UserAgent);

            keyValues.Add(period.ToString());

            var id = string.Join("_", keyValues);
            var idBytes = Encoding.UTF8.GetBytes(id);
            var hashBytes = new System.Security.Cryptography.SHA1Managed().ComputeHash(idBytes);
            var hex = BitConverter.ToString(hashBytes).Replace("-", "");
            return hex;
        }

        internal string GetClientIp(HttpRequestBase request)
        {
            return IpAddressParser.GetClientIp(request);
        }
    }
}