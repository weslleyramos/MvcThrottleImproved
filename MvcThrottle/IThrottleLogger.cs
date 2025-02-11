﻿namespace MvcThrottleImproved
{
    /// <summary>
    /// Log requests that exceed the limit
    /// </summary>
    public interface IThrottleLogger
    {
        void Log(ThrottleLogEntry entry);
    }
}
