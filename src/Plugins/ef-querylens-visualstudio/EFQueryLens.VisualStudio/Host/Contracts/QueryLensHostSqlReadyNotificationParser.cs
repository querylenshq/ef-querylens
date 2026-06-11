using Newtonsoft.Json.Linq;

namespace EFQueryLens.VisualStudio.Host.Contracts;

internal static class QueryLensHostSqlReadyNotificationParser
{
    public static QueryLensHostSqlReadyNotification? Parse(JToken? methodParam)
    {
        var token = UnwrapParameter(methodParam);
        if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return null;
        }

        return token.ToObject<QueryLensHostSqlReadyNotification>();
    }

    internal static JToken? UnwrapParameter(JToken? methodParam)
    {
        if (methodParam is JArray array)
        {
            return array.Count > 0 ? array[0] : null;
        }

        return methodParam;
    }
}
