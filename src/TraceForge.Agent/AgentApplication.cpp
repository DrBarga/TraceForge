#include "AgentApplication.h"
#include "NamedPipeServer.h"

namespace traceforge::agent
{
    ExitCode AgentApplication::Run(std::wstring_view pipeName) const
    {
        NamedPipeServer server{std::wstring(pipeName)};
        server.Run();
        return ExitCode::Success;
    }
}
