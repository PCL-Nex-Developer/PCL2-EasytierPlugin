using System;

namespace PCL.EasyTierPlugin.Scaffolding.Exceptions;

public class FailedToGetPlayerException : Exception
{
    public FailedToGetPlayerException() : base()
    {
    }

    public FailedToGetPlayerException(string msg) : base(msg)
    {
    }
}