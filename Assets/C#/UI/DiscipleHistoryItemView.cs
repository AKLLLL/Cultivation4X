using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DiscipleHistoryItemView : MonoBehaviour
{
    [SerializeField] private Image typeMarker;
    [SerializeField] private TMP_Text typeText;
    [SerializeField] private TMP_Text headingText;
    [SerializeField] private TMP_Text bodyText;

    public void Configure(Image marker, TMP_Text typeLabel, TMP_Text heading, TMP_Text body)
    {
        typeMarker = marker;
        typeText = typeLabel;
        headingText = heading;
        bodyText = body;
    }

    public void Bind(DiscipleHistoryItemSnapshot snapshot)
    {
        typeText.text = snapshot.type;
        headingText.text = snapshot.heading;
        bodyText.text = snapshot.body;
        Color typeColor = TypeColor(snapshot.type);
        typeMarker.color = snapshot.isMajor
            ? Color.Lerp(typeColor, new Color(0.92f, 0.68f, 0.24f, 1f), 0.45f)
            : typeColor;
        headingText.color = snapshot.isMajor
            ? new Color(0.96f, 0.79f, 0.40f, 1f)
            : new Color(0.86f, 0.84f, 0.74f, 1f);
        Image background = GetComponent<Image>();
        if (background != null) background.color = snapshot.isMajor
            ? new Color(0.13f, 0.105f, 0.055f, 0.98f)
            : new Color(0.055f, 0.095f, 0.073f, 0.98f);
    }

    private static Color TypeColor(string type)
    {
        if (type == "任务") return new Color(0.42f, 0.64f, 0.49f, 1f);
        if (type == "事件") return new Color(0.72f, 0.53f, 0.22f, 1f);
        if (type == "决策") return new Color(0.40f, 0.58f, 0.68f, 1f);
        if (type == "受伤" || type == "生死") return new Color(0.68f, 0.27f, 0.20f, 1f);
        return new Color(0.55f, 0.47f, 0.30f, 1f);
    }
}
