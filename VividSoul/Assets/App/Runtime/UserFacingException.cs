#nullable enable

using System;

namespace VividSoul.Runtime
{
    public sealed class UserFacingException : Exception
    {
        public UserFacingException(string message)
            : base(message)
        {
        }
    }
}
