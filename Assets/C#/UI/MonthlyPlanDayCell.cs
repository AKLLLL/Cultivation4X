using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MonthlyPlanDayCell : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerUpHandler
{
    private static MonthlyPlanDayCell active;
    private int day;
    private Image background;
    private TMP_Text label;
    private Action<MonthlyPlanDayCell, int> paint;
    private Action complete;

    public void Configure(int dayOfCycle, Image cellBackground, TMP_Text cellLabel,
        Action<MonthlyPlanDayCell, int> paintAction, Action completeAction)
    {
        day = dayOfCycle;
        background = cellBackground;
        label = cellLabel;
        paint = paintAction;
        complete = completeAction;
    }

    public void Render(MonthlyActivityType activity, Color color, string activityName)
    {
        if (background != null) background.color = color;
        if (label != null) label.text = $"{day}\n{activityName}";
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        active = this;
        PaintSelf();
        eventData.Use();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (active == null || active == this) return;
        PaintSelf();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (active == null || eventData.button != PointerEventData.InputButton.Left) return;
        complete?.Invoke();
        CancelDrag();
        eventData.Use();
    }

    private void PaintSelf() => paint?.Invoke(this, day);

    private void OnDisable()
    {
        if (active == this) CancelDrag();
    }

    public static void CancelDrag() => active = null;
}
