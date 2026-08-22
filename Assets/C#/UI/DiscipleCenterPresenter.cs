public sealed class DiscipleCenterPresenter
{
    private readonly DiscipleCenterView view;
    private string selectedCharacterId;
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
        subscribed = false;
    }

    private void Subscribe()
    {
        if (subscribed) return;
        if (NPCManager.Instance != null) NPCManager.Instance.OnRosterChanged += Refresh;
        if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDayPassed;
        subscribed = true;
    }

    private void HandleDayPassed(int _) => Refresh();

    private void Refresh()
    {
        DiscipleCenterSnapshot snapshot = DiscipleCenterSnapshotBuilder.Build(selectedCharacterId);
        selectedCharacterId = snapshot.selectedCharacterId;
        view.Render(snapshot);
    }
}
