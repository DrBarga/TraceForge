#pragma once

#include "ProcessInfo.h"

#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace traceforge::agent::json
{
    [[nodiscard]] std::string ExtractType(std::string_view request);
    [[nodiscard]] std::string BuildPongResponse();
    [[nodiscard]] std::string BuildShutdownResponse();
    [[nodiscard]] std::string BuildErrorResponse(std::string_view message);
    [[nodiscard]] std::string BuildSnapshotResponse(
        const std::vector<ProcessInfo>& processes,
        std::int64_t timestampUnixMs);
}
