using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MvcThrottleImproved;

namespace MvcThrottle.Demo.Controllers
{
    [EnableThrottling]
    public class BaseController : Controller
    {

    }
}
