using MvcThrottle.Repositories;
using System.Web.Mvc;
using System.Web.Routing;

namespace MvcThrottle.Demo.Helpers
{
    public class MvcThrottleCustomFilter : ThrottlingFilter
    {
        public MvcThrottleCustomFilter(ThrottlePolicy policy, IThrottleRepository throttleRepository, IThrottleLogger logger)
            : base(policy, throttleRepository, logger)
        {
            this.QuotaExceededMessage = "API calls quota exceeded! maximum admitted {0} per {1}.";
        }

        protected override ActionResult QuotaExceededResult(RequestContext filterContext, string message, System.Net.HttpStatusCode responseCode, string requestId)
        {
            //var result = new ViewResult
            //{
            //    ViewName = "RateLimited",
            //    ViewData = {["Message"] = message}
            //};

            var result = new JsonResult
            {
                Data = message,
                JsonRequestBehavior = JsonRequestBehavior.AllowGet
            };

            return result;
        }
    }
}