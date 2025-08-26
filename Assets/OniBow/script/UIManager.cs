using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class UIManager : MonoBehaviour
{
    [Header("플레이어 이동 버튼")]
    [SerializeField] private Button rButton;
    [SerializeField] private Button lButton;

    private PlayerControl _playerControl;

    private void Start()
    {
        _playerControl = FindAnyObjectByType<PlayerControl>();

        // 오른쪽 버튼에 이벤트 트리거 설정
        if (rButton != null)
        {
            AddEventTrigger(rButton.gameObject, () => _playerControl.StartMoving(1f), () => _playerControl.StopMoving());
        }

        // 왼쪽 버튼에 이벤트 트리거 설정
        if (lButton != null)
        {
            AddEventTrigger(lButton.gameObject, () => _playerControl.StartMoving(-1f), () => _playerControl.StopMoving());
        }
    }

    /// <summary>
    /// 게임 오브젝트에 PointerDown, PointerUp, PointerExit 이벤트를 추가하는 헬퍼 메서드입니다.
    /// </summary>
    /// <param name="target">이벤트를 추가할 대상 게임 오브젝트</param>
    /// <param name="onPointerDown">마우스를 눌렀을 때 실행할 액션</param>
    /// <param name="onPointerUp">마우스를 떼거나 영역을 벗어났을 때 실행할 액션</param>
    private void AddEventTrigger(GameObject target, System.Action onPointerDown, System.Action onPointerUp)
    {
        EventTrigger trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
        trigger.triggers.Clear();

        // PointerDown 이벤트 설정
        var pointerDownEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDownEntry.callback.AddListener((data) => { onPointerDown?.Invoke(); });
        trigger.triggers.Add(pointerDownEntry);

        // PointerUp 이벤트 설정
        var pointerUpEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUpEntry.callback.AddListener((data) => { onPointerUp?.Invoke(); });
        trigger.triggers.Add(pointerUpEntry);

        // PointerExit 이벤트 설정 (포인터가 버튼 밖으로 나갔을 때)
        var pointerExitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        pointerExitEntry.callback.AddListener((data) => { onPointerUp?.Invoke(); }); // 동일하게 StopMoving 호출
        trigger.triggers.Add(pointerExitEntry);
    }
}
