public sealed class DiscipleCenterPresenter
{
    private readonly DiscipleCenterView view;
    private string selectedCharacterId;
    private string renderedActivityLabel;
    private bool subscribed;

    public DiscipleCenterPresenter(DiscipleCenterView view)
    {
        this.view = view;
    }

    public void Open(string characterId)
    {
        if (!string.IsNullOrWhiteSpace(characterId)) selectedCharacterId = characterId;
        Subscribe();
        Refresh();
    }

    public void Select(string characterId)
    {
        selectedCharacterId = characterId;
        Refresh();
    }

    public void Close()
    {
        if (!subscribed) return;
        if (NPCManager.Instance != null) NPCManager.Instance.OnRosterChanged -= Refresh;
        if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDayPassed;
        if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted -= HandleDayStarted;
        if (TimeManager.Instance != null) TimeManager.Instance.OnHourChanged -= HandleHourChanged;
        subscribed = false;
    }

    private void Subscribe()
    {
        if (subscribed) return;
        if (NPCManager.Instance != null) NPCManager.Instance.OnRosterChanged += Refresh;
        if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDayPassed;
        if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted += HandleDayStarted;
        if (TimeManager.Instance != null) TimeManager.Instance.OnHourChanged += HandleHourChanged;
        subscribed = true;
    }

    private void HandleDayPassed(int _) => Refresh();

    private void HandleDayStarted(GameDateTime _) => Refresh();

    private void HandleHourChanged(GameDateTime _)
    {
        string currentActivityLabel = CurrentActivityLabel();
        if (!string.Equals(currentActivityLabel, renderedActivityLabel, System.StringComparison.Ordinal)) Refresh();
    }

    private string CurrentActivityLabel() =>
        string.IsNullOrWhiteSpace(selectedCharacterId)
            ? null
            : TimeManager.Instance?.GetCurrentActivityLabel(selectedCharacterId);

    private void Refresh()
    {
        DiscipleCenterSnapshot snapshot = DiscipleCenterSnapshotBuilder.Build(selectedCharacterId);
        selectedCharacterId = snapshot.selectedCharacterId;
        renderedActivityLabel = CurrentActivityLabel();
        view.Render(snapshot);
    }
}
