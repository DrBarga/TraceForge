#pragma once

#include "ProcessInfo.h"
#include "ProcessInspection.h"
#include "ProcessWatcher.h"

#include <cstdint>
#include <string>
#include <string_view>
#include <vector>
#include <optional>

namespace traceforge::agent::json
{
    struct Request
    {
        std::string type;
        std::optional<std::uint32_t> processId;
    };

    [[nodiscard]] Request ParseRequest(std::string_view request);
    [[nodiscard]] std::string BuildPongResponse();
    [[nodiscard]] std::string BuildShutdownResponse();
    [[nodiscard]] std::string BuildErrorResponse(std::string_view message);
    [[nodiscard]] std::string BuildDumpResponse(std::wstring_view path);
    [[nodiscard]] std::string BuildWatchResponse(std::uint32_t processId);
    [[nodiscard]] std::string BuildUnwatchResponse();
    [[nodiscard]] std::string BuildInspectionResponse(const ProcessInspection& inspection);
    [[nodiscard]] std::string BuildSnapshotResponse(
        const std::vector<ProcessInfo>& processes,
        std::int64_t timestampUnixMs,
        const std::optional<ProcessExit>& processExit = std::nullopt);
}
