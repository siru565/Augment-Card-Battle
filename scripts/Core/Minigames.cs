using System;
using System.Collections.Generic;

namespace SpCardgame.Core;

/// <summary>차례 시작 때 직접 조작해야 하는 미니게임 종류입니다.</summary>
public enum MinigameKind
{
    /// <summary>컬링: 카드(스톤)를 튕겨서 하우스 정중앙(버튼)에 세우면 승리합니다.</summary>
    Curling,

    /// <summary>정밀 사수: 빠르게 왕복하는 바늘을 황금 구간에서 멈춥니다. 연속으로 성공하면 승리합니다.</summary>
    Marksman,

    /// <summary>예언자: 다음 내 차례가 올 때 바닥 문양을 예언합니다.</summary>
    Oracle,
}

/// <summary>
/// 지금 진행 중인 미니게임입니다. 모두에게 공개되는 정보라 PlayerView로 전달되고,
/// 조작하는 사람 말고 다른 사람들 화면에도 똑같이 보여 줍니다.
/// A, B, C는 종류마다 뜻이 다릅니다.
/// - 컬링: A = 하우스 가로 위치, B = 회전(휘는 정도), C = 사용 안 함
/// - 정밀 사수: A = 바늘 속도(초당 왕복), B = 바늘 시작 위치, C = 황금 구간 가운데 (구간 너비는 Level로 정해짐)
/// - 예언자: 사용 안 함
/// </summary>
public sealed record MinigameInfo(int Id, MinigameKind Kind, int Player, float A, float B, float C, int Level);

/// <summary>
/// 공개되는 승리 조건 진행도입니다. (다른 사람 화면에도 보입니다)
/// Detail은 짧은 부가 설명입니다. (예: 도미노의 다음 숫자, 예언한 문양)
/// </summary>
public sealed record WinGoal(string Augment, int Value, int Target, string Detail = "");

/// <summary>
/// 컬링 물리 계산입니다. 방장과 모든 참가자가 같은 식으로 계산해서 같은 궤적을 봅니다.
/// 좌표: 가로 x는 -0.5 ~ 0.5(시트 폭), 세로 y는 0(던지는 곳) → 1.2(뒷선). 하우스 가운데는 (TargetX, HouseY)입니다.
/// </summary>
public static class CurlingSim
{
    public const float HouseY = 1.0f;
    public const float HouseRadius = 0.16f;
    public const float ButtonRadius = 0.035f;
    public const float SheetHalfWidth = 0.5f;
    public const float BackLine = 1.2f;

    /// <summary>얼음 마찰로 줄어드는 속도(초당)입니다.</summary>
    public const float Friction = 0.55f;

    /// <summary>회전값 1일 때 옆으로 휘는 가속도입니다.</summary>
    public const float CurlForce = 0.25f;

    public const float MinSpeed = 0.6f;
    public const float MaxSpeed = 1.4f;

    /// <summary>한 번 계산할 때의 시간 간격(초)입니다.</summary>
    public const float Step = 1f / 120f;

    public enum Outcome
    {
        /// <summary>시트 밖으로 나가거나 하우스에 못 미쳤습니다.</summary>
        Miss,

        /// <summary>하우스(큰 원) 안에 멈췄습니다.</summary>
        House,

        /// <summary>버튼(정중앙)에 멈췄습니다. 승리!</summary>
        Button,
    }

    public readonly record struct Result(float X, float Y, float Distance, Outcome Outcome);

    /// <summary>
    /// 조준(aim, -1 ~ 1: 왼쪽 ~ 오른쪽)과 힘(power, 0 ~ 1)으로 던졌을 때 스톤이 멈추는 곳을 계산합니다.
    /// path를 넘기면 지나간 점들을 담아 줍니다. (화면 연출용)
    /// </summary>
    public static Result Simulate(float targetX, float curl, float aim, float power, List<(float X, float Y)>? path = null)
    {
        aim = Math.Clamp(aim, -1f, 1f);
        power = Math.Clamp(power, 0f, 1f);
        float angle = aim * 0.45f;
        float speed = MinSpeed + (MaxSpeed - MinSpeed) * power;
        float vx = MathF.Sin(angle) * speed;
        float vy = MathF.Cos(angle) * speed;
        float x = 0f, y = 0f;
        path?.Add((x, y));

        for (int i = 0; i < 2000; i++)
        {
            float s = MathF.Sqrt(vx * vx + vy * vy);
            if (s < 0.01f)
            {
                break;
            }

            // 마찰은 진행 방향 반대로, 회전은 진행 방향의 옆으로 작용합니다.
            float dec = Math.Min(Friction * Step, s);
            vx -= vx / s * dec;
            vy -= vy / s * dec;
            vx += CurlForce * curl * Step;

            x += vx * Step;
            y += vy * Step;
            if (i % 4 == 0)
            {
                path?.Add((x, y));
            }

            if (MathF.Abs(x) > SheetHalfWidth || y > BackLine)
            {
                path?.Add((x, y));
                return new Result(x, y, float.MaxValue, Outcome.Miss);
            }
        }

        path?.Add((x, y));
        float dx = x - targetX;
        float dy = y - HouseY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        var outcome = distance <= ButtonRadius ? Outcome.Button : distance <= HouseRadius ? Outcome.House : Outcome.Miss;
        return new Result(x, y, distance, outcome);
    }

    /// <summary>
    /// 가장 좋은 조준과 힘을 찾습니다. (봇이 쓰는 계산입니다. 사람은 눈과 손으로 맞춰야 합니다)
    /// </summary>
    public static (float Aim, float Power) BestThrow(float targetX, float curl)
    {
        float bestAim = 0, bestPower = 0.5f, best = float.MaxValue;
        for (int pass = 0; pass < 3; pass++)
        {
            float aimSpan = pass == 0 ? 1f : pass == 1 ? 0.1f : 0.01f;
            float powerSpan = pass == 0 ? 0.5f : pass == 1 ? 0.05f : 0.005f;
            float centerAim = pass == 0 ? 0 : bestAim;
            float centerPower = pass == 0 ? 0.5f : bestPower;
            for (int i = -10; i <= 10; i++)
            {
                for (int j = -10; j <= 10; j++)
                {
                    float aim = centerAim + aimSpan * i / 10f;
                    float power = centerPower + powerSpan * j / 10f;
                    var result = Simulate(targetX, curl, aim, power);
                    if (result.Distance < best)
                    {
                        best = result.Distance;
                        bestAim = aim;
                        bestPower = power;
                    }
                }
            }
        }

        return (bestAim, bestPower);
    }
}

/// <summary>
/// 정밀 사수의 바늘 계산입니다. 바늘은 0 → 1 → 0으로 왕복합니다.
/// 단계(Level)가 오를수록 바늘이 빨라지고 황금 구간을 통과하는 시간이 짧아집니다.
/// </summary>
public static class MarksmanRules
{
    /// <summary>연속으로 이만큼 맞히면 승리합니다.</summary>
    public const int Target = 5;

    /// <summary>바늘이 저절로 멈추는 시간(초)입니다. 이때까지 못 멈추면 실패입니다.</summary>
    public const float TimeLimit = 6f;

    /// <summary>단계별 바늘 속도(초당 왕복 횟수)입니다.</summary>
    private static readonly float[] Speeds = { 2.0f, 2.4f, 2.8f, 3.2f, 3.6f };

    /// <summary>단계별로 바늘이 황금 구간 안에 머무는 시간(초)입니다. 1프레임(약 0.017초)에 가까울 만큼 짧습니다.</summary>
    private static readonly float[] Windows = { 0.045f, 0.038f, 0.032f, 0.027f, 0.023f };

    public static float Speed(int level) => Speeds[Math.Clamp(level, 0, Speeds.Length - 1)];

    /// <summary>황금 구간 너비(0~1 비율)입니다. 바늘이 1초에 2 × 속도만큼 움직이므로, 너비 = 2 × 속도 × 머무는 시간입니다.</summary>
    public static float ZoneWidth(int level) => 2f * Speed(level) * Windows[Math.Clamp(level, 0, Windows.Length - 1)];

    /// <summary>시작 후 t초가 지났을 때 바늘 위치(0~1)입니다.</summary>
    public static float Position(float speed, float start, float t)
    {
        float u = start + t * speed;
        float frac = u - MathF.Floor(u);
        return 1f - MathF.Abs(2f * frac - 1f);
    }

    public static bool IsHit(MinigameInfo game, float t)
    {
        if (t < 0 || t > TimeLimit)
        {
            return false;
        }

        float position = Position(game.A, game.B, t);
        return MathF.Abs(position - game.C) <= ZoneWidth(game.Level) / 2f;
    }
}

/// <summary>잭팟 슬롯머신입니다. 릴 3개가 모두 ★(프리즘)이면 승리합니다.</summary>
public static class JackpotRules
{
    /// <summary>릴 기호입니다. 0~3은 문양(불꽃·달빛·숲·물결), 4는 ★입니다.</summary>
    public const int Star = 4;

    /// <summary>★가 나올 확률입니다. ★★★ 확률은 이 값의 세제곱입니다.</summary>
    public const double StarChance = 0.17;

    public static int Spin(Random rng)
    {
        if (rng.NextDouble() < StarChance)
        {
            return Star;
        }

        return rng.Next(4);
    }

    public static string SymbolName(int symbol) => symbol == Star ? "★" : Card.ColorName((CardColor)symbol);
}

/// <summary>도미노와 예언자의 목표치입니다.</summary>
public static class StreakRules
{
    /// <summary>도미노: 내가 낸 숫자가 이만큼 연속으로(+1씩) 이어지면 승리합니다.</summary>
    public const int DominoTarget = 4;

    /// <summary>예언자: 이만큼 연속으로 맞히면 승리합니다.</summary>
    public const int OracleTarget = 4;

    /// <summary>컬링: 카드를 이만큼 내면 한 번 던질 수 있습니다.</summary>
    public const int CurlingCharge = 8;
}
