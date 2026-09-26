using System;
using System.Collections.Generic;

namespace SpCardgame.Audio;

/// <summary>
/// 효과음을 코드로 합성합니다. (녹음 파일 없이 사인파·노이즈·필터·메아리만으로 만듭니다)
/// Godot에 의존하지 않으므로 콘솔에서도 소리 데이터를 만들어 검사할 수 있습니다.
/// </summary>
public static class SfxSynth
{
    public const int SampleRate = 44100;

    public enum Wave
    {
        Sine,
        Triangle,
        Square,
        Saw,
    }

    /// <summary>게임에서 쓰는 효과음 이름 목록입니다. audio/sfx 폴더에 같은 이름의 파일을 넣으면 그 파일로 바뀝니다.</summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "play", "play_action", "draw", "hover", "click", "my_turn", "attack", "warn", "skip", "reverse",
        "awaken", "ability", "augment_offer", "augment_gain", "shuffle", "shield", "win", "lose", "error", "deal",
    };

    // ───────────── 기본 재료 ─────────────

    private static float[] New(double seconds) => new float[(int)(seconds * SampleRate)];

    private static float Osc(Wave wave, double phase)
    {
        double p = phase - Math.Floor(phase);
        return wave switch
        {
            Wave.Sine => (float)Math.Sin(p * Math.Tau),
            Wave.Triangle => (float)(1 - 4 * Math.Abs(p - 0.5)),
            Wave.Square => p < 0.5 ? 0.6f : -0.6f,
            _ => (float)(2 * p - 1) * 0.7f,
        };
    }

    /// <summary>
    /// 음 하나를 더합니다. 주파수는 f0에서 f1로 지수적으로 미끄러지고,
    /// 소리 크기는 attack 동안 올라갔다가 decay 시간 상수로 줄어듭니다.
    /// </summary>
    private static void Tone(float[] buf, double start, double dur, double f0, double f1, Wave wave, float amp,
        double attack = 0.005, double decay = 0.15, double vibrato = 0)
    {
        int s0 = (int)(start * SampleRate);
        int n = (int)(dur * SampleRate);
        double phase = 0;
        for (int i = 0; i < n && s0 + i < buf.Length; i++)
        {
            double t = (double)i / SampleRate;
            double k = t / dur;
            double f = f0 * Math.Pow(f1 / f0, k);
            if (vibrato > 0)
            {
                f *= 1 + vibrato * Math.Sin(t * Math.Tau * 7);
            }

            phase += f / SampleRate;
            buf[s0 + i] += Osc(wave, phase) * amp * Env(t, dur, attack, decay);
        }
    }

    /// <summary>
    /// 걸러 낸 잡음을 더합니다. 필터 차단 주파수가 lp0에서 lp1로 움직여서 '휙', '쾅' 같은 느낌을 냅니다.
    /// </summary>
    private static void Noise(float[] buf, double start, double dur, float amp, double lp0, double lp1,
        double attack = 0.002, double decay = 0.05, int seed = 1)
    {
        var rng = new Random(seed);
        int s0 = (int)(start * SampleRate);
        int n = (int)(dur * SampleRate);
        double y = 0;
        for (int i = 0; i < n && s0 + i < buf.Length; i++)
        {
            double t = (double)i / SampleRate;
            double cutoff = lp0 * Math.Pow(lp1 / lp0, t / dur);
            double a = 1 - Math.Exp(-Math.Tau * cutoff / SampleRate);
            y += a * (rng.NextDouble() * 2 - 1 - y);
            buf[s0 + i] += (float)y * amp * 2.2f * Env(t, dur, attack, decay);
        }
    }

    /// <summary>종소리처럼 배음이 섞인 음입니다. (마법, 증강 연출)</summary>
    private static void Bell(float[] buf, double start, double freq, float amp, double decay = 0.5)
    {
        Tone(buf, start, decay * 4, freq, freq, Wave.Sine, amp, 0.002, decay);
        Tone(buf, start, decay * 2.5, freq * 2.76, freq * 2.76, Wave.Sine, amp * 0.35f, 0.002, decay * 0.5);
        Tone(buf, start, decay * 2, freq * 5.4, freq * 5.4, Wave.Sine, amp * 0.15f, 0.002, decay * 0.3);
    }

    private static float Env(double t, double dur, double attack, double decay)
    {
        double a = t < attack ? t / attack : Math.Exp(-(t - attack) / decay);
        double tail = dur - t < 0.01 ? (dur - t) / 0.01 : 1; // 끝부분 딸깍 소리를 막습니다.
        return (float)(a * Math.Max(0, tail));
    }

    /// <summary>메아리를 더해서 공간감을 줍니다.</summary>
    private static void Echo(float[] buf, double delay, double feedback, int repeats = 4)
    {
        int d = (int)(delay * SampleRate);
        for (int r = 0; r < repeats; r++)
        {
            for (int i = buf.Length - 1; i >= d; i--)
            {
                buf[i] += buf[i - d] * (float)feedback;
            }
        }
    }

    private static float[] Finish(float[] buf, float peak = 0.8f)
    {
        float max = 0;
        foreach (float v in buf)
        {
            max = Math.Max(max, Math.Abs(v));
        }

        if (max > 0)
        {
            float gain = peak / max;
            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] *= gain;
            }
        }

        return buf;
    }

    private static double Note(int semitonesFromA4) => 440 * Math.Pow(2, semitonesFromA4 / 12.0);

    // ───────────── 효과음 ─────────────

    /// <summary>이름에 맞는 효과음을 만듭니다. -1~1 범위의 모노 샘플입니다.</summary>
    public static float[] Make(string name)
    {
        switch (name)
        {
            case "play":
            {
                // 카드를 탁 내려놓는 소리: 짧은 잡음 + 낮은 쿵
                var b = New(0.16);
                Noise(b, 0, 0.09, 0.55f, 4200, 700, 0.001, 0.025, 3);
                Tone(b, 0, 0.1, 190, 85, Wave.Sine, 0.7f, 0.002, 0.035);
                return Finish(b, 0.7f);
            }

            case "play_action":
            {
                var b = New(0.3);
                Noise(b, 0, 0.09, 0.55f, 4200, 700, 0.001, 0.025, 4);
                Tone(b, 0, 0.1, 200, 80, Wave.Sine, 0.7f, 0.002, 0.04);
                Tone(b, 0.02, 0.22, 660, 990, Wave.Triangle, 0.28f, 0.005, 0.07);
                return Finish(b, 0.75f);
            }

            case "draw":
            {
                // 카드를 스윽 끌어오는 소리
                var b = New(0.18);
                Noise(b, 0, 0.16, 0.5f, 500, 5000, 0.04, 0.05, 7);
                return Finish(b, 0.5f);
            }

            case "hover":
            {
                var b = New(0.04);
                Tone(b, 0, 0.035, 2200, 2000, Wave.Sine, 0.2f, 0.001, 0.008);
                return Finish(b, 0.25f);
            }

            case "click":
            {
                var b = New(0.07);
                Tone(b, 0, 0.06, 1100, 750, Wave.Triangle, 0.4f, 0.001, 0.015);
                Noise(b, 0, 0.015, 0.2f, 6000, 3000, 0.001, 0.004, 9);
                return Finish(b, 0.45f);
            }

            case "my_turn":
            {
                // 딩-동 (내 차례 알림)
                var b = New(0.7);
                Tone(b, 0, 0.35, Note(3), Note(3), Wave.Triangle, 0.5f, 0.004, 0.09);
                Tone(b, 0.1, 0.5, Note(10), Note(10), Wave.Triangle, 0.5f, 0.004, 0.13);
                Echo(b, 0.09, 0.25, 3);
                return Finish(b, 0.55f);
            }

            case "attack":
            {
                // 퍽! 무거운 타격
                var b = New(0.55);
                Tone(b, 0, 0.45, 170, 38, Wave.Sine, 1f, 0.002, 0.12);
                Noise(b, 0, 0.2, 0.8f, 6000, 350, 0.001, 0.05, 11);
                Tone(b, 0, 0.14, 95, 60, Wave.Square, 0.25f, 0.002, 0.05);
                return Finish(b, 0.9f);
            }

            case "warn":
            {
                // 삐빅 (직접 뽑아야 해요)
                var b = New(0.34);
                Tone(b, 0, 0.1, 330, 330, Wave.Square, 0.3f, 0.003, 0.05);
                Tone(b, 0.14, 0.12, 440, 440, Wave.Square, 0.3f, 0.003, 0.06);
                return Finish(b, 0.45f);
            }

            case "skip":
            {
                var b = New(0.3);
                Tone(b, 0, 0.26, 780, 300, Wave.Triangle, 0.5f, 0.003, 0.08);
                Noise(b, 0, 0.1, 0.2f, 3000, 800, 0.002, 0.03, 13);
                return Finish(b, 0.6f);
            }

            case "reverse":
            {
                var b = New(0.4);
                Tone(b, 0, 0.16, 380, 950, Wave.Triangle, 0.45f, 0.004, 0.08);
                Tone(b, 0.15, 0.2, 950, 380, Wave.Triangle, 0.45f, 0.004, 0.08);
                Noise(b, 0, 0.3, 0.2f, 800, 4000, 0.05, 0.1, 15);
                return Finish(b, 0.6f);
            }

            case "awaken":
            {
                // 각성: 빠르게 올라가는 반짝임 → 쿵! → 긴 울림
                var b = New(2.2);
                int[] steps = { 3, 7, 10, 15, 19, 22, 27 };
                for (int i = 0; i < steps.Length; i++)
                {
                    Bell(b, i * 0.045, Note(steps[i]), 0.25f, 0.35);
                }

                Noise(b, 0, 0.35, 0.25f, 800, 9000, 0.25, 0.1, 17);
                Tone(b, 0.33, 1.1, 130, 32, Wave.Sine, 1.1f, 0.003, 0.3);
                Noise(b, 0.33, 0.7, 0.8f, 2500, 150, 0.002, 0.15, 19);
                Tone(b, 0.33, 1.4, Note(3), Note(3), Wave.Saw, 0.12f, 0.02, 0.45);
                Tone(b, 0.33, 1.4, Note(10), Note(10), Wave.Saw, 0.1f, 0.02, 0.45);
                Echo(b, 0.14, 0.3, 4);
                return Finish(b, 0.95f);
            }

            case "ability":
            {
                var b = New(1.2);
                Bell(b, 0, Note(15), 0.5f, 0.3);
                Bell(b, 0.08, Note(22), 0.4f, 0.3);
                Noise(b, 0, 0.4, 0.15f, 3000, 10000, 0.1, 0.12, 21);
                Echo(b, 0.11, 0.3, 4);
                return Finish(b, 0.7f);
            }

            case "augment_offer":
            {
                // 반짝이며 올라가는 아르페지오
                var b = New(1.3);
                int[] steps = { 0, 3, 7, 12, 15, 19, 24, 27 };
                for (int i = 0; i < steps.Length; i++)
                {
                    Tone(b, i * 0.06, 0.4, Note(steps[i] + 3), Note(steps[i] + 3), Wave.Triangle, 0.35f, 0.003, 0.1);
                }

                Echo(b, 0.12, 0.3, 4);
                return Finish(b, 0.65f);
            }

            case "augment_gain":
            {
                var b = New(1.6);
                Bell(b, 0, Note(3), 0.4f, 0.45);
                Bell(b, 0.02, Note(7), 0.35f, 0.45);
                Bell(b, 0.04, Note(10), 0.35f, 0.45);
                Bell(b, 0.12, Note(15), 0.3f, 0.5);
                Noise(b, 0, 0.5, 0.12f, 6000, 12000, 0.05, 0.15, 23);
                Echo(b, 0.13, 0.3, 3);
                return Finish(b, 0.75f);
            }

            case "shuffle":
            {
                var b = New(0.6);
                for (int i = 0; i < 7; i++)
                {
                    Noise(b, i * 0.065, 0.07, 0.45f, 1500, 5500, 0.01, 0.02, 30 + i);
                }

                return Finish(b, 0.5f);
            }

            case "shield":
            {
                var b = New(0.6);
                Tone(b, 0, 0.5, 1900, 2100, Wave.Sine, 0.4f, 0.002, 0.12, 0.01);
                Tone(b, 0, 0.4, 2850, 3150, Wave.Sine, 0.2f, 0.002, 0.08);
                Echo(b, 0.07, 0.25, 2);
                return Finish(b, 0.5f);
            }

            case "win":
            {
                // 빰빰빰 빠~ (팡파르)
                var b = New(2.0);
                int[] steps = { -2, 3, 7, 10 };
                for (int i = 0; i < steps.Length; i++)
                {
                    double len = i == steps.Length - 1 ? 0.9 : 0.14;
                    Tone(b, i * 0.13, len, Note(steps[i]), Note(steps[i]), Wave.Square, 0.25f, 0.005, i == 3 ? 0.35 : 0.08);
                    Tone(b, i * 0.13, len, Note(steps[i]), Note(steps[i]), Wave.Triangle, 0.35f, 0.005, i == 3 ? 0.35 : 0.08);
                }

                Tone(b, 0.39, 0.9, Note(3), Note(3), Wave.Triangle, 0.25f, 0.01, 0.35);
                Tone(b, 0.39, 0.9, Note(7), Note(7), Wave.Triangle, 0.2f, 0.01, 0.35);
                Noise(b, 0.39, 0.6, 0.12f, 7000, 12000, 0.02, 0.2, 41);
                Echo(b, 0.15, 0.25, 3);
                return Finish(b, 0.8f);
            }

            case "lose":
            {
                var b = New(1.4);
                int[] steps = { 7, 6, 5 };
                for (int i = 0; i < steps.Length; i++)
                {
                    Tone(b, i * 0.28, i == 2 ? 0.8 : 0.28, Note(steps[i] - 12), Note(steps[i] - 12) * (i == 2 ? 0.97 : 1), Wave.Triangle, 0.5f, 0.01, i == 2 ? 0.3 : 0.12, i == 2 ? 0.012 : 0);
                }

                return Finish(b, 0.6f);
            }

            case "error":
            {
                var b = New(0.2);
                Tone(b, 0, 0.16, 150, 130, Wave.Square, 0.35f, 0.003, 0.06);
                return Finish(b, 0.4f);
            }

            case "deal":
            {
                var b = New(0.9);
                for (int i = 0; i < 12; i++)
                {
                    Noise(b, i * 0.06, 0.05, 0.4f, 2500, 6000, 0.005, 0.015, 50 + i);
                }

                return Finish(b, 0.45f);
            }

            default:
                return New(0.01);
        }
    }

    /// <summary>16비트 PCM 바이트로 바꿉니다. (Godot AudioStreamWav 데이터 형식)</summary>
    public static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)(v & 0xff);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }

        return bytes;
    }
}
