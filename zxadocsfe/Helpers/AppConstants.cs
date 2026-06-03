using System;

namespace zxadocsfe.Helpers;

public class AppConstants
{
    public enum StateKey
    {
        TOKEN,
        REFRESH_TOKEN
    }
    public enum FolderType
    {
        Inbox,
        Outbox,
        Archive,
        Delete
    }

    public enum SessionVariable
    {
        CurrentUser
    }

    public record SessionVariables
    {
        public const string TOKEN = "UserToken";
        public const string REFRESH_TOKEN = "RefereshToken";
    }
    public record HttpSchemes
    {
        public const string Core = "Api";
    }

    public record AttachmentType
    {
        public const string Signature = "Signature";
        public const string Comment = "Comment";
        public const string Document = "Document";
    }


}