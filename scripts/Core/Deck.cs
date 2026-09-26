using System;
using System.Collections.Generic;

namespace SpCardgame.Core;

/// <summary>
/// 덱 구성과 셔플을 담당합니다.
/// </summary>
public static class Deck
{
    public static readonly CardColor[] Colors =
    {
        CardColor.Red, CardColor.Yellow, CardColor.Green, CardColor.Blue,
    };

    /// <summary>새 특수 카드는 색마다 이 장수만큼 들어갑니다.</summary>
    public const int SpecialCopiesPerColor = 1;

    /// <summary>덱에 섞는 증강 카드 장수입니다. 예전 랜덤 증강을 없애서 지금은 0장입니다.</summary>
    public const int AugmentCardCount = 0;

    /// <summary>각성 카드는 문양마다 이 장수만큼 들어갑니다.</summary>
    public const int AwakenCopiesPerColor = 2;

    /// <summary>문양마다 들어가는 +카드 구성입니다.</summary>
    public static readonly CardKind[] DrawKinds =
    {
        CardKind.DrawOne, CardKind.DrawOne, CardKind.DrawTwo, CardKind.DrawThree,
    };

    private static readonly CardKind[] SpecialKinds =
    {
        CardKind.Swap, CardKind.Seal, CardKind.Copy, CardKind.Frenzy,
    };

    /// <summary>
    /// 프로토타입 덱을 만듭니다. 기본 116장(+카드 16장 포함) + 특수 카드 16장 + 각성 카드 8장 = 140장입니다.
    /// </summary>
    public static List<Card> BuildPrototype()
    {
        var cards = BuildStandard();
        int nextId = cards.Count;

        foreach (var color in Colors)
        {
            foreach (var kind in SpecialKinds)
            {
                for (int i = 0; i < SpecialCopiesPerColor; i++)
                {
                    cards.Add(new Card(nextId++, color, kind));
                }
            }

            for (int i = 0; i < AwakenCopiesPerColor; i++)
            {
                cards.Add(new Card(nextId++, color, CardKind.Awaken));
            }
        }

        for (int i = 0; i < AugmentCardCount; i++)
        {
            cards.Add(new Card(nextId++, CardColor.Wild, CardKind.Augment));
        }

        return cards;
    }

    /// <summary>
    /// 기본 우노 108장을 생성합니다.
    /// 색마다 0 한 장, 1~9 두 장씩, 스킵·리버스·+2 두 장씩이며, 와일드와 와일드+4가 4장씩 들어갑니다.
    /// </summary>
    public static List<Card> BuildStandard()
    {
        var cards = new List<Card>(108);
        int nextId = 0;

        foreach (var color in Colors)
        {
            cards.Add(new Card(nextId++, color, CardKind.Number, 0));

            for (int n = 1; n <= 9; n++)
            {
                cards.Add(new Card(nextId++, color, CardKind.Number, n));
                cards.Add(new Card(nextId++, color, CardKind.Number, n));
            }

            for (int i = 0; i < 2; i++)
            {
                cards.Add(new Card(nextId++, color, CardKind.Skip));
                cards.Add(new Card(nextId++, color, CardKind.Reverse));
            }

            // +카드: 문양마다 +1 두 장, +2 한 장, +3 한 장입니다. (같은 + 숫자끼리 합산할 수 있습니다)
            foreach (var kind in DrawKinds)
            {
                cards.Add(new Card(nextId++, color, kind));
            }
        }

        for (int i = 0; i < 4; i++)
        {
            cards.Add(new Card(nextId++, CardColor.Wild, CardKind.Wild));
            cards.Add(new Card(nextId++, CardColor.Wild, CardKind.WildDrawFour));
        }

        return cards;
    }

    /// <summary>
    /// Fisher-Yates 방식으로 섞습니다. 같은 시드를 넣으면 항상 같은 순서가 나와서 버그 재현에 유용합니다.
    /// </summary>
    public static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
