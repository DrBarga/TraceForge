#include "JsonProtocol.h"

#include <Windows.h>

#include <iomanip>
#include <sstream>
#include <charconv>
#include <limits>
#include <stdexcept>

namespace
{
    [[nodiscard]] std::string ToUtf8(std::wstring_view value)
    {
        if (value.empty())
        {
            return {};
        }

        const int size = WideCharToMultiByte(
            CP_UTF8,
            0,
            value.data(),
            static_cast<int>(value.size()),
            nullptr,
            0,
            nullptr,
            nullptr);

        if (size <= 0)
        {
            return {};
        }

        std::string result(static_cast<std::size_t>(size), '\0');
        WideCharToMultiByte(
            CP_UTF8,
            0,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            size,
            nullptr,
            nullptr);
        return result;
    }

    [[nodiscard]] std::string EscapeJson(std::string_view value)
    {
        std::string result;
        result.reserve(value.size() + 16);

        for (const unsigned char character : value)
        {
            switch (character)
            {
            case '"': result += "\\\""; break;
            case '\\': result += "\\\\"; break;
            case '\b': result += "\\b"; break;
            case '\f': result += "\\f"; break;
            case '\n': result += "\\n"; break;
            case '\r': result += "\\r"; break;
            case '\t': result += "\\t"; break;
            default:
                if (character < 0x20)
                {
                    std::ostringstream escaped;
                    escaped << "\\u"
                            << std::hex
                            << std::setw(4)
                            << std::setfill('0')
                            << static_cast<int>(character);
                    result += escaped.str();
                }
                else
                {
                    result.push_back(static_cast<char>(character));
                }
                break;
            }
        }

        return result;
    }
}

namespace traceforge::agent::json
{
    Request ParseRequest(std::string_view input)
    {
        std::size_t offset = 0;
        const auto skipWhitespace = [&]()
        {
            while (offset < input.size() &&
                   (input[offset] == ' ' || input[offset] == '\t' ||
                    input[offset] == '\r' || input[offset] == '\n'))
            {
                ++offset;
            }
        };
        const auto readString = [&]() -> std::string
        {
            if (offset >= input.size() || input[offset++] != '"')
            {
                throw std::invalid_argument("Expected a JSON string.");
            }

            const auto start = offset;
            while (offset < input.size() && input[offset] != '"')
            {
                if (input[offset] == '\\' || static_cast<unsigned char>(input[offset]) < 0x20)
                {
                    throw std::invalid_argument("Escaped or control characters are not valid in Agent request fields.");
                }
                ++offset;
            }

            if (offset == input.size())
            {
                throw std::invalid_argument("Unterminated JSON string.");
            }

            const auto result = std::string(input.substr(start, offset - start));
            ++offset;
            return result;
        };

        skipWhitespace();
        if (offset >= input.size() || input[offset++] != '{')
        {
            throw std::invalid_argument("Agent request must be a JSON object.");
        }

        Request request;
        bool hasType = false;
        bool hasPid = false;
        skipWhitespace();
        while (offset < input.size() && input[offset] != '}')
        {
            const auto key = readString();
            skipWhitespace();
            if (offset >= input.size() || input[offset++] != ':')
            {
                throw std::invalid_argument("Expected a colon in Agent request.");
            }
            skipWhitespace();

            if (key == "type" && !hasType)
            {
                request.type = readString();
                hasType = true;
            }
            else if (key == "pid" && !hasPid)
            {
                const char* first = input.data() + offset;
                const char* last = input.data() + input.size();
                std::uint64_t value = 0;
                const auto [end, error] = std::from_chars(first, last, value);
                if (error != std::errc{} || end == first ||
                    value > std::numeric_limits<std::uint32_t>::max())
                {
                    throw std::invalid_argument("Invalid process ID in Agent request.");
                }
                offset += static_cast<std::size_t>(end - first);
                request.processId = static_cast<std::uint32_t>(value);
                hasPid = true;
            }
            else
            {
                throw std::invalid_argument("Unknown or duplicate Agent request field.");
            }

            skipWhitespace();
            if (offset < input.size() && input[offset] == ',')
            {
                ++offset;
                skipWhitespace();
                if (offset < input.size() && input[offset] == '}')
                {
                    throw std::invalid_argument("Trailing comma in Agent request.");
                }
            }
            else if (offset >= input.size() || input[offset] != '}')
            {
                throw std::invalid_argument("Expected a comma or closing brace in Agent request.");
            }
        }

        if (offset >= input.size() || input[offset++] != '}')
        {
            throw std::invalid_argument("Unterminated Agent request.");
        }
        skipWhitespace();
        if (offset != input.size() || !hasType || request.type.empty())
        {
            throw std::invalid_argument("Invalid Agent request.");
        }

        return request;
    }

    std::string BuildPongResponse()
    {
        return R"({"type":"pong","protocolVersion":1})";
    }

    std::string BuildShutdownResponse()
    {
        return R"({"type":"shutdown"})";
    }

    std::string BuildErrorResponse(std::string_view message)
    {
        return "{\"type\":\"error\",\"message\":\"" + EscapeJson(message) + "\"}";
    }

    std::string BuildDumpResponse(std::wstring_view path)
    {
        return "{\"type\":\"dump\",\"path\":\"" + EscapeJson(ToUtf8(path)) + "\"}";
    }

    std::string BuildWatchResponse(std::uint32_t processId)
    {
        return "{\"type\":\"watch\",\"pid\":" + std::to_string(processId) + "}";
    }

    std::string BuildUnwatchResponse()
    {
        return R"({"type":"unwatch"})";
    }

    std::string BuildInspectionResponse(const ProcessInspection& inspection)
    {
        std::ostringstream output;
        output << "{\"type\":\"inspection\",\"pid\":" << inspection.processId
               << ",\"startTimeUnixMs\":" << inspection.startTimeUnixMs
               << ",\"identityError\":" << inspection.identityError
               << ",\"threadError\":" << inspection.threadError
               << ",\"moduleError\":" << inspection.moduleError
               << ",\"networkError\":" << inspection.networkError
               << ",\"threads\":[";

        bool first = true;
        for (const auto& thread : inspection.threads)
        {
            if (!first) output << ',';
            first = false;
            output << "{\"id\":" << thread.id << ",\"basePriority\":" << thread.basePriority << '}';
        }

        output << "],\"modules\":[";
        first = true;
        for (const auto& module : inspection.modules)
        {
            if (!first) output << ',';
            first = false;
            output << "{\"name\":\"" << EscapeJson(ToUtf8(module.name))
                   << "\",\"path\":\"" << EscapeJson(ToUtf8(module.path))
                   << "\",\"sizeBytes\":" << module.sizeBytes << '}';
        }

        output << "],\"connections\":[";
        first = true;
        for (const auto& connection : inspection.connections)
        {
            if (!first) output << ',';
            first = false;
            output << "{\"protocol\":\"" << EscapeJson(connection.protocol)
                   << "\",\"localAddress\":\"" << EscapeJson(connection.localAddress)
                   << "\",\"localPort\":" << connection.localPort
                   << ",\"remoteAddress\":\"" << EscapeJson(connection.remoteAddress)
                   << "\",\"remotePort\":" << connection.remotePort
                   << ",\"state\":\"" << EscapeJson(connection.state) << "\"}";
        }

        output << "]}";
        return output.str();
    }

    std::string BuildSnapshotResponse(
        const std::vector<ProcessInfo>& processes,
        std::int64_t timestampUnixMs,
        const std::optional<ProcessExit>& processExit)
    {
        std::ostringstream output;
        output << std::fixed << std::setprecision(2);
        output << "{\"type\":\"snapshot\",\"timestampUnixMs\":"
               << timestampUnixMs
               << ",\"processes\":[";

        bool first = true;
        for (const auto& process : processes)
        {
            if (!first)
            {
                output << ',';
            }
            first = false;

            output << "{\"pid\":" << process.processId
                   << ",\"parentPid\":" << process.parentProcessId
                   << ",\"startTimeUnixMs\":" << process.startTimeUnixMs
                   << ",\"name\":\"" << EscapeJson(ToUtf8(process.name)) << '"'
                   << ",\"path\":\"" << EscapeJson(ToUtf8(process.executablePath)) << '"'
                   << ",\"cpuPercent\":" << process.cpuPercent
                   << ",\"workingSetBytes\":" << process.workingSetBytes
                   << ",\"threadCount\":" << process.threadCount
                   << ",\"accessible\":" << (process.accessible ? "true" : "false")
                   << ",\"accessError\":" << process.accessError
                   << ",\"pathAvailable\":" << (process.pathAvailable ? "true" : "false")
                   << ",\"pathError\":" << process.pathError
                   << ",\"cpuAvailable\":" << (process.cpuAvailable ? "true" : "false")
                   << ",\"cpuError\":" << process.cpuError
                   << ",\"memoryAvailable\":" << (process.memoryAvailable ? "true" : "false")
                   << ",\"memoryError\":" << process.memoryError
                   << '}';
        }

        output << ']';
        if (processExit.has_value())
        {
            output << ",\"processExit\":{\"pid\":" << processExit->processId
                   << ",\"exitCode\":" << processExit->exitCode
                   << ",\"timestampUnixMs\":" << processExit->timestampUnixMs
                   << '}';
        }

        output << '}';
        return output.str();
    }
}
