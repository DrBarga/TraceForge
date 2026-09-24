#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace traceforge::agent
{
    struct ThreadDetails final
    {
        std::uint32_t id{};
        std::int32_t basePriority{};
    };

    struct ModuleDetails final
    {
        std::wstring name;
        std::wstring path;
        std::uint32_t sizeBytes{};
    };

    struct ConnectionDetails final
    {
        std::string protocol;
        std::string localAddress;
        std::uint16_t localPort{};
        std::string remoteAddress;
        std::uint16_t remotePort{};
        std::string state;
    };

    struct ProcessInspection final
    {
        std::uint32_t processId{};
        std::int64_t startTimeUnixMs{};
        std::vector<ThreadDetails> threads;
        std::vector<ModuleDetails> modules;
        std::vector<ConnectionDetails> connections;
        std::uint32_t threadError{};
        std::uint32_t moduleError{};
        std::uint32_t networkError{};
        std::uint32_t identityError{};
    };

    class ProcessInspector final
    {
    public:
        [[nodiscard]] static ProcessInspection Inspect(std::uint32_t processId);
    };
}
