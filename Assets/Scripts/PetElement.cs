using UnityEngine;

/// 펫의 원소 태그 — 이 컴포넌트를 선택하면 그 원소에 맞는 설정만 모아서 보인다.
/// (실제 UI 는 Editor/PetElementEditor.cs — 재질·이펙트를 펫에서 바로 조절)
public class PetElement : MonoBehaviour
{
    public enum Element { Metal, Wood, Stone, Water, Fire, Lightning }
    public Element element = Element.Metal;

    public Material Mat
    {
        get { var mr = GetComponent<MeshRenderer>(); return mr != null ? mr.sharedMaterial : null; }
    }
}
