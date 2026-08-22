using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DiscipleListItemView : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text realmText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private Image selectionImage;
    [SerializeField] private Button button;

    public void Configure(TMP_Text newNameText, TMP_Text newRealmText, TMP_Text newStateText,
        Image newSelectionImage, Button newButton)
    {
        nameText = newNameText;
        realmText = newRealmText;
        stateText = newStateText;
        selectionImage = newSelectionImage;
        button = newButton;
    }

    public void Bind(DiscipleListItemSnapshot snapshot, bool selected,
        UnityEngine.Events.UnityAction onClick)
    {
        ConfigureSingleLine(nameText);
        ConfigureSingleLine(realmText);
        ConfigureSingleLine(stateText);
        nameText.text = snapshot.name;
        realmText.text = snapshot.realm;
        stateText.text = snapshot.state;
        Image stateBackground = stateText.transform.parent.GetComponent<Image>();
        if (stateBackground != null) stateBackground.color = StateColor(snapshot.state);
        selectionImage.color = selected
            ? new Color(0.24f, 0.25f, 0.15f, 0.98f)
            : new Color(0.075f, 0.105f, 0.085f, 0.96f);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(onClick);
    }

    private static void ConfigureSingleLine(TMP_Text text)
    {
        if (text == null) return;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static Color StateColor(string state)
    {
        if (state == "空闲") return new Color(0.13f, 0.34f, 0.23f, 1f);
        if (state == "忙碌" || state == "闭关" || state == "外出")
            return new Color(0.42f, 0.32f, 0.10f, 1f);
        if (state == "养伤") return new Color(0.45f, 0.16f, 0.12f, 1f);
        return new Color(0.18f, 0.25f, 0.20f, 1f);
    }
}
