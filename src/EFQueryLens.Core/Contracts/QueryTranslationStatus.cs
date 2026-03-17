namespace EFQueryLens.Core.Contracts;

public enum QueryTranslationStatus
{
    Ready = 0,
    InQueue = 1,
    Starting = 2,
    DaemonUnavailable = 3,
}