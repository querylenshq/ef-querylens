namespace Share.Common.Workflow.Core.Domain;

public static partial class Constants
{
    public static class Schema
    {
        public const string Workflow = nameof(Workflow);
        public const string Application = nameof(Application);
        public const string Auth = nameof(Auth);
        public const string Companies = nameof(Companies);
    }

    public static class MedicsAuth
    {
        public static class Claims
        {
            public const string CompanyId = "company-id";
            public const string CompanyName = "company-name";
            public const string CompanyUen = "company-uen";

            public const string MedicsRole = "medics-role";
            public const string Email = "email";
        }
    }
}
