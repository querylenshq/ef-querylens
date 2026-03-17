// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

internal static class QueryLensErrorCodes
{
    internal const string DaemonRestartClientNotReady = "QL2001_DAEMON_RESTART_CLIENT_NOT_READY";
    internal const string DaemonRestartRpcNotReady = "QL2002_DAEMON_RESTART_RPC_NOT_READY";
    internal const string DaemonRestartFailed = "QL2003_DAEMON_RESTART_FAILED";
    internal const string DaemonRestartIncomplete = "QL2004_DAEMON_RESTART_INCOMPLETE";
    internal const string CommandExecutionFailed = "QL2005_COMMAND_EXECUTION_FAILED";
}
