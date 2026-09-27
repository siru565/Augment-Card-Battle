using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// Godot 번역 서버에 끼워 넣는 영어 번역입니다.
/// 라벨과 버튼은 글자를 보여 줄 때 자동으로 번역 서버에 묻는데, 그때 Loc 번역기로 영어를 돌려줍니다.
/// 그래서 씬 파일과 코드의 한국어 글자를 따로 바꾸지 않아도 화면이 영어로 나옵니다.
/// </summary>
public partial class LocTranslation : Translation
{
    public override StringName _GetMessage(StringName srcMessage, StringName context)
    {
        string source = srcMessage.ToString();
        string english = Loc.ToEnglish(source);
        return english == source ? new StringName("") : new StringName(english);
    }

    public override StringName _GetPluralMessage(StringName srcMessage, StringName srcPluralMessage, int n, StringName context) =>
        _GetMessage(n == 1 ? srcMessage : srcPluralMessage, context);
}
