﻿using System;
namespace Raven.Abstractions.Exceptions
{
    public class ConcurrentLoadTimeoutException : Exception
    {
        public ConcurrentLoadTimeoutException(string message) : base(message)
        {
        }

        public ConcurrentLoadTimeoutException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if !DNXCORE50
        protected ConcurrentLoadTimeoutException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
#endif
    }
