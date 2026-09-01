using UnityEngine;

public sealed class SectOrganizationBodyView : MonoBehaviour
{
    [SerializeField] private RectTransform content;

    public RectTransform Content => content;

    public void Configure(RectTransform targetContent)
    {
        content = targetContent;
    }
}
