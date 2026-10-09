// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract.Runtime;

internal static class PlainText
{
    internal static bool IsUnsafe(char character)
    {
        return char.IsControl(character) || character is '\u200e' or '\u200f'
            or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069';
    }
}
