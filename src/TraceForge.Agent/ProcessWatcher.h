#pragma once

#include "UniqueHandle.h"

#include <cstdint>
#include <optional>

namespace traceforge::agent
{
    struct ProcessExit final
    {
        std::uint32_t processId{};
        std::uint32_t exitCode{};
        std::int64_t timestampUnixMs{};
    };

    class ProcessWatcher final
    {
    public:
        void Watch(std::uint32_t processId);
        void Stop() noexcept;
        [[nodiscard]] std::optional<ProcessExit> Poll();

    private:
        UniqueHandle process_;
        std::uint32_t processId_{};
    };
}
