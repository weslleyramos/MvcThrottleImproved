using MvcThrottleImproved.Enums;

namespace MvcThrottleImproved.Extensions
{
    public static class RateLimitPeriodExtensions
    {
        public static string ToLang(this RateLimitPeriod period, Language language = Language.EN)
        {
            var value = "";

            switch (period)
            {
                case RateLimitPeriod.Week:
                    switch (language)
                    {
                        case Language.PT_BR:
                            value = "semana";
                            break;
                        default:
                            value = RateLimitPeriod.Week.ToString("G").ToLower();
                            break;
                    }
                    break;

                case RateLimitPeriod.Day:
                    switch (language)
                    {
                        case Language.PT_BR:
                            value = "dia";
                            break;
                        default:
                            value = RateLimitPeriod.Day.ToString("G").ToLower();
                            break;
                    }
                    break;

                case RateLimitPeriod.Hour:
                    switch (language)
                    {
                        case Language.PT_BR:
                            value = "hora";
                            break;
                        default:
                            value = RateLimitPeriod.Hour.ToString("G").ToLower();
                            break;
                    }
                    break;

                case RateLimitPeriod.Minute:
                    switch (language)
                    {
                        case Language.PT_BR:
                            value = "minuto";
                            break;
                        default:
                            value = RateLimitPeriod.Minute.ToString("G").ToLower();
                            break;
                    }
                    break;

                case RateLimitPeriod.Second:
                    switch (language)
                    {
                        case Language.PT_BR:
                            value = "segundo";
                            break;
                        default:
                            value = RateLimitPeriod.Second.ToString("G").ToLower();
                            break;
                    }
                    break;
            }
            return value;
        }
    }
}