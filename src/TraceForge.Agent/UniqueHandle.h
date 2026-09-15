#pragma once

#include <Windows.h>

namespace traceforge::agent
{
    class UniqueHandle final
    {
    public:
        UniqueHandle() noexcept = default;

        explicit UniqueHandle(HANDLE handle) noexcept
            : handle_(handle)
        {
        }

        ~UniqueHandle()
        {
            Reset();
        }

        UniqueHandle(const UniqueHandle&) = delete;
        UniqueHandle& operator=(const UniqueHandle&) = delete;

        UniqueHandle(UniqueHandle&& other) noexcept
            : handle_(other.Release())
        {
        }

        UniqueHandle& operator=(UniqueHandle&& other) noexcept
        {
            if (this != &other)
            {
                Reset(other.Release());
            }

            return *this;
        }

        [[nodiscard]] HANDLE Get() const noexcept
        {
            return handle_;
        }

        [[nodiscard]] bool IsValid() const noexcept
        {
            return handle_ != nullptr && handle_ != INVALID_HANDLE_VALUE;
        }

        [[nodiscard]] HANDLE Release() noexcept
        {
            const HANDLE handle = handle_;
            handle_ = nullptr;
            return handle;
        }

        void Reset(HANDLE handle = nullptr) noexcept
        {
            if (IsValid())
            {
                CloseHandle(handle_);
            }

            handle_ = handle;
        }

    private:
        HANDLE handle_ = nullptr;
    };
}
