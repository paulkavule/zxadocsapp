using System;

namespace zxadocsfe.Helpers;

public class AppConstants
{
    public enum StateKey
    {
        TOKEN,
        REFRESH_TOKEN
    }

    public record HttpSchemes
    {
        public const string Core = "Api";
    }

}