using System;
using System.Collections.Generic;

namespace SpCardgame.Util;

/// <summary>
/// 움직이는 GIF를 프레임별 RGBA 이미지로 풀어 주는 작은 디코더입니다.
/// Godot는 GIF를 직접 읽지 못해서 Steam 움직이는 아바타를 재생할 때 사용합니다. (Godot 의존 없음)
/// </summary>
public static class GifDecoder
{
    /// <summary>풀어 낸 한 프레임입니다. Rgba는 전체 화면 크기(Width×Height×4)입니다.</summary>
    public sealed record Frame(byte[] Rgba, int DelayMs);

    /// <summary>풀어 낸 GIF 전체입니다.</summary>
    public sealed record Animation(int Width, int Height, List<Frame> Frames);

    /// <summary>
    /// GIF 바이트를 풉니다. 형식이 잘못되었으면 null을 돌려줍니다.
    /// maxFrames를 넘는 프레임은 버려서 메모리를 아낍니다.
    /// </summary>
    public static Animation? Decode(byte[] data, int maxFrames = 200)
    {
        try
        {
            return DecodeInternal(data, maxFrames);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Animation? DecodeInternal(byte[] data, int maxFrames)
    {
        if (data.Length < 13 || data[0] != 'G' || data[1] != 'I' || data[2] != 'F')
        {
            return null;
        }

        int pos = 6;
        int width = data[pos] | (data[pos + 1] << 8);
        int height = data[pos + 2] | (data[pos + 3] << 8);
        byte flags = data[pos + 4];
        pos += 7;

        if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
        {
            return null;
        }

        byte[]? globalTable = null;
        if ((flags & 0x80) != 0)
        {
            int size = 3 * (1 << ((flags & 0x07) + 1));
            globalTable = new byte[size];
            Array.Copy(data, pos, globalTable, 0, size);
            pos += size;
        }

        var canvas = new byte[width * height * 4];
        var frames = new List<Frame>();

        // 다음 이미지에 적용할 그래픽 제어 확장 값입니다.
        int delayMs = 100;
        int transparentIndex = -1;
        int disposal = 0;

        while (pos < data.Length && frames.Count < maxFrames)
        {
            byte block = data[pos++];
            if (block == 0x3B)
            {
                // 파일 끝입니다.
                break;
            }

            if (block == 0x21)
            {
                byte label = data[pos++];
                if (label == 0xF9 && data[pos] >= 4)
                {
                    byte gceFlags = data[pos + 1];
                    int delay = data[pos + 2] | (data[pos + 3] << 8);
                    // 브라우저처럼 0~1 (1/100초)은 너무 빨라서 100ms로 봅니다.
                    delayMs = delay <= 1 ? 100 : delay * 10;
                    transparentIndex = (gceFlags & 0x01) != 0 ? data[pos + 4] : -1;
                    disposal = (gceFlags >> 2) & 0x07;
                }

                pos = SkipSubBlocks(data, pos);
                continue;
            }

            if (block != 0x2C)
            {
                // 알 수 없는 블록이면 여기까지 읽은 프레임만 씁니다.
                break;
            }

            int left = data[pos] | (data[pos + 1] << 8);
            int top = data[pos + 2] | (data[pos + 3] << 8);
            int fw = data[pos + 4] | (data[pos + 5] << 8);
            int fh = data[pos + 6] | (data[pos + 7] << 8);
            byte imgFlags = data[pos + 8];
            pos += 9;

            byte[]? table = globalTable;
            if ((imgFlags & 0x80) != 0)
            {
                int size = 3 * (1 << ((imgFlags & 0x07) + 1));
                table = new byte[size];
                Array.Copy(data, pos, table, 0, size);
                pos += size;
            }

            bool interlaced = (imgFlags & 0x40) != 0;
            int minCodeSize = data[pos++];

            // 이미지 데이터 서브 블록들을 하나로 이어 붙입니다.
            var compressed = new List<byte>();
            while (pos < data.Length)
            {
                int len = data[pos++];
                if (len == 0)
                {
                    break;
                }

                for (int i = 0; i < len && pos + i < data.Length; i++)
                {
                    compressed.Add(data[pos + i]);
                }

                pos += len;
            }

            var indices = Lzw(compressed, minCodeSize, fw * fh);

            // "이전 상태로 되돌리기"는 그리기 전 화면을 저장해 둡니다.
            byte[]? saved = disposal == 3 ? (byte[])canvas.Clone() : null;

            if (table != null)
            {
                DrawFrame(canvas, width, height, indices, table, left, top, fw, fh, interlaced, transparentIndex);
            }

            frames.Add(new Frame((byte[])canvas.Clone(), delayMs));

            // 다음 프레임을 위한 정리(disposal)입니다.
            if (disposal == 2)
            {
                ClearRect(canvas, width, height, left, top, fw, fh);
            }
            else if (disposal == 3 && saved != null)
            {
                canvas = saved;
            }

            delayMs = 100;
            transparentIndex = -1;
            disposal = 0;
        }

        return frames.Count == 0 ? null : new Animation(width, height, frames);
    }

    private static int SkipSubBlocks(byte[] data, int pos)
    {
        while (pos < data.Length)
        {
            int len = data[pos++];
            if (len == 0)
            {
                break;
            }

            pos += len;
        }

        return pos;
    }

    private static void DrawFrame(byte[] canvas, int width, int height, byte[] indices, byte[] table,
        int left, int top, int fw, int fh, bool interlaced, int transparentIndex)
    {
        int colors = table.Length / 3;
        for (int i = 0; i < fw * fh && i < indices.Length; i++)
        {
            int index = indices[i];
            if (index == transparentIndex || index >= colors)
            {
                continue;
            }

            int row = i / fw;
            int col = i % fw;
            int y = top + (interlaced ? InterlacedRow(row, fh) : row);
            int x = left + col;
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                continue;
            }

            int o = (y * width + x) * 4;
            canvas[o] = table[index * 3];
            canvas[o + 1] = table[index * 3 + 1];
            canvas[o + 2] = table[index * 3 + 2];
            canvas[o + 3] = 255;
        }
    }

    /// <summary>인터레이스 GIF는 8·8·4·2줄 간격 4번에 나눠 저장되어 있어서, n번째로 저장된 줄의 실제 위치를 계산합니다.</summary>
    private static int InterlacedRow(int n, int height)
    {
        int pass1 = (height + 7) / 8;
        if (n < pass1)
        {
            return n * 8;
        }

        n -= pass1;
        int pass2 = (height + 3) / 8;
        if (n < pass2)
        {
            return n * 8 + 4;
        }

        n -= pass2;
        int pass3 = (height + 1) / 4;
        if (n < pass3)
        {
            return n * 4 + 2;
        }

        n -= pass3;
        return n * 2 + 1;
    }

    private static void ClearRect(byte[] canvas, int width, int height, int left, int top, int fw, int fh)
    {
        for (int y = Math.Max(0, top); y < Math.Min(height, top + fh); y++)
        {
            for (int x = Math.Max(0, left); x < Math.Min(width, left + fw); x++)
            {
                int o = (y * width + x) * 4;
                canvas[o] = canvas[o + 1] = canvas[o + 2] = canvas[o + 3] = 0;
            }
        }
    }

    /// <summary>GIF의 LZW 압축을 풀어 색 번호 배열을 만듭니다.</summary>
    private static byte[] Lzw(List<byte> input, int minCodeSize, int pixelCount)
    {
        var output = new byte[pixelCount];
        int outPos = 0;

        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;

        // 각 코드는 (앞 코드, 마지막 글자, 길이)로 저장합니다.
        var prefix = new int[4096];
        var suffix = new byte[4096];
        var length = new int[4096];
        for (int i = 0; i < clearCode; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
            length[i] = 1;
        }

        int codeSize = minCodeSize + 1;
        int nextCode = endCode + 1;
        int previous = -1;

        int bitBuffer = 0;
        int bitCount = 0;
        int inPos = 0;
        var stack = new byte[4096];

        while (outPos < pixelCount)
        {
            while (bitCount < codeSize)
            {
                if (inPos >= input.Count)
                {
                    return output;
                }

                bitBuffer |= input[inPos++] << bitCount;
                bitCount += 8;
            }

            int code = bitBuffer & ((1 << codeSize) - 1);
            bitBuffer >>= codeSize;
            bitCount -= codeSize;

            if (code == clearCode)
            {
                codeSize = minCodeSize + 1;
                nextCode = endCode + 1;
                previous = -1;
                continue;
            }

            if (code == endCode)
            {
                break;
            }

            int current = code;
            byte first;
            if (code < nextCode && (code < clearCode || code > endCode))
            {
                // 이미 사전에 있는 코드입니다.
                first = WriteCode(code, prefix, suffix, length, stack, output, ref outPos);
            }
            else if (code == nextCode && previous >= 0)
            {
                // 사전에 아직 없는 코드: 앞 코드 + 앞 코드의 첫 글자입니다.
                byte prevFirst = FirstOf(previous, prefix, suffix);
                first = WriteCode(previous, prefix, suffix, length, stack, output, ref outPos);
                if (outPos < pixelCount)
                {
                    output[outPos++] = prevFirst;
                }

                current = -2;
            }
            else
            {
                // 깨진 데이터입니다.
                break;
            }

            if (previous >= 0 && nextCode < 4096)
            {
                prefix[nextCode] = previous;
                suffix[nextCode] = current == -2 ? FirstOf(previous, prefix, suffix) : first;
                length[nextCode] = length[previous] + 1;
                nextCode++;
                if (nextCode == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }

            previous = code;
        }

        return output;
    }

    private static byte FirstOf(int code, int[] prefix, byte[] suffix)
    {
        while (prefix[code] >= 0)
        {
            code = prefix[code];
        }

        return suffix[code];
    }

    /// <summary>코드 하나를 글자들로 펼쳐 출력에 적고, 첫 글자를 돌려줍니다.</summary>
    private static byte WriteCode(int code, int[] prefix, byte[] suffix, int[] length, byte[] stack, byte[] output, ref int outPos)
    {
        int n = 0;
        int c = code;
        while (c >= 0 && n < stack.Length)
        {
            stack[n++] = suffix[c];
            c = prefix[c];
        }

        for (int i = n - 1; i >= 0 && outPos < output.Length; i--)
        {
            output[outPos++] = stack[i];
        }

        return stack[n - 1];
    }
}
