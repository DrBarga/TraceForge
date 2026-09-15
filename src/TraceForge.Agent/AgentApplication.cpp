#include "AgentApplication.h"
#include "NamedPipeServer.h"

namespace traceforge::agent
{
    ExitCode AgentApplication::Run() const
    {
        NamedPipeServer server;
        server.Run();
        return ExitCode::Success;
    }
}
