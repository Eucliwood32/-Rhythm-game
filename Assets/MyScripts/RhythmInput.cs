// ============================================================================
// RhythmInput.cs : 키보드 입력을 감지해서 "어느 레인이 눌렸는지" 알리는 컴포넌트
// ----------------------------------------------------------------------------
// 6요소 중 담당: Player Input (플레이어 입력)
// 하는 일: A키 → 상단 레인, B키 → 하단 레인이 눌렸다는 사실을 이벤트로 알린다.
// 안 하는 일: 판정. "잘 쳤는지"는 전혀 모른다. 그건 Judge의 일이다.
//
// 왜 입력만 따로 떼어 두나?
//   키 배치를 바꾸거나(예: J/K), 게임패드·터치를 추가할 때 이 파일만 고치면 된다.
//   Judge는 "Upper가 눌렸다"는 사실만 받으니 아무것도 바꿀 필요가 없다.
//
// 연결:
//   RhythmInput.OnPressed --> Judge.HandlePressed (판정, LastPressLatency로 실제 누른 시각 보정)
//                         --> DebugOverlay.HandlePressed (화면 로그)
//   Judge는 롱노트 판정 때 IsHeld()로 "지금도 누르고 있는지"를 직접 물어본다.
// ============================================================================

using System;                    // Action<T> 대리자 타입이 여기 있다.
using UnityEngine;               // MonoBehaviour
using UnityEngine.InputSystem;   // 새 Input System 패키지. Keyboard.current, InputAction을 쓰기 위해 필요하다.
using UnityEngine.InputSystem.LowLevel;   // InputState.currentTime (입력 시스템의 현재 시각)
                                 // (옛날 방식인 Input.GetKeyDown은 쓰지 않는다. 프로젝트가 새 Input System으로 설정돼 있다.)

// 레인 종류. Upper = 0, Lower = 1.
// 다른 스크립트들이 (int)lane으로 배열 인덱스로 쓰기 때문에 이 순서와 값이 중요하다.
public enum Lane
{
    Upper,  // 상단 레인: A키, 슈퍼 드래곤 라이징 어퍼컷
    Lower   // 하단 레인: B키, 헥토파스칼 드릴 초전도체 킥
}

/// <summary>A/B 키 입력을 레인 이벤트로 바꿔서 알린다. 판정은 하지 않는다.</summary>
// 클래스 이름이 PlayerInput이 아닌 이유:
//   Input System 패키지에 이미 PlayerInput이라는 클래스가 있어서 이름이 겹치면 컴파일 에러가 난다.
public class RhythmInput : MonoBehaviour   // MonoBehaviour를 상속해야 GameObject에 붙일 수 있고 OnEnable 등이 불린다.
{
    // event: 다른 스크립트가 "이 일이 생기면 나를 불러줘"라고 등록(구독)하는 창구.
    // Action<Lane> = "Lane 하나를 받고 아무것도 반환하지 않는 함수"의 타입.
    // 구독: input.OnPressed += 내함수;   해제: input.OnPressed -= 내함수;
    // event 키워드가 붙으면 바깥에서는 +=, -=만 할 수 있고 직접 호출(Invoke)은 이 클래스 안에서만 가능하다.
    public event Action<Lane> OnPressed;

    /// <summary>해당 레인 키를 지금 이 순간 누르고 있는지. Judge가 롱노트 유지 확인에 쓴다.</summary>
    public bool IsHeld(Lane lane)
    {
        var keyboard = Keyboard.current;   // 현재 연결된 키보드. 키보드가 없는 환경(일부 모바일 등)이면 null.
        if (keyboard == null) return false;   // 키보드가 없으면 "안 누르고 있음"으로 친다.

        // isPressed: 키가 눌려 있는 동안 매 프레임 true. (wasPressedThisFrame은 누른 그 프레임 한 번만 true)
        // 삼항 연산자 "조건 ? A : B" = 조건이 참이면 A, 거짓이면 B.
        return lane == Lane.Upper ? keyboard.aKey.isPressed : keyboard.bKey.isPressed;
    }

    /// <summary>
    /// 방금 알린 입력이 실제로 눌린 순간부터 지금까지 지난 시간(초). Judge가 이만큼 빼서 "실제로 누른 순간"의 곡 시간으로 판정한다.
    /// </summary>
    public double LastPressLatency { get; private set; }

    // InputAction: 키 하나(또는 여러 개)를 "행동"으로 묶은 것. 상태 변화가 생길 때마다 콜백을 불러 준다.
    InputAction upperAction, lowerAction;

    // 예전 방식(Update에서 wasPressedThisFrame 확인)의 문제:
    //   1) 프레임마다 "지금 눌려 있나"를 비교하는 방식이라, 한 프레임 안에 눌렀다 뗀 짧은 입력은 놓칠 수 있다.
    //      렉이 걸려 프레임이 길어지면(편집기에서 Debug.Log가 많을 때 등) ABAB 빠른 연타에서 입력이 씹힌다.
    //   2) 실제로 누른 순간이 아니라 "다음 Update가 돈 순간"을 누른 시각으로 쓰므로, 최대 한 프레임만큼 늦게 판정된다.
    // 지금 방식: 키 입력 하나하나(이벤트)마다 콜백이 불리고, 그 입력이 실제로 들어온 시각(ctx.time)을 알 수 있다.
    void OnEnable()
    {
        // new InputAction(이름, 종류, 바인딩): Button 종류는 누르는 순간 performed 콜백이 불린다.
        upperAction = new InputAction("Upper", InputActionType.Button, "<Keyboard>/a");
        lowerAction = new InputAction("Lower", InputActionType.Button, "<Keyboard>/b");
        // 람다 ctx => ...: 콜백이 불리면 어느 레인인지와 입력 정보(ctx)를 Fire에 넘긴다.
        upperAction.performed += ctx => Fire(Lane.Upper, ctx);
        lowerAction.performed += ctx => Fire(Lane.Lower, ctx);
        upperAction.Enable();   // Enable해야 입력을 받기 시작한다.
        lowerAction.Enable();
    }

    void OnDisable()
    {
        // 다 쓴 InputAction은 끄고 정리한다(Dispose). 안 하면 오브젝트가 사라져도 콜백이 남을 수 있다.
        upperAction?.Disable(); upperAction?.Dispose();
        lowerAction?.Disable(); lowerAction?.Dispose();
    }

    // 키 하나가 눌렸을 때. A와 B를 같은 순간에 눌러도 각각 따로 불린다(동시치기).
    void Fire(Lane lane, InputAction.CallbackContext ctx)
    {
        // InputState.currentTime: 입력 시스템 기준 "지금" 시각, ctx.time: 그 키가 실제로 눌린 시각. 같은 시계라 빼면 지연 시간이 나온다.
        // 보통 0 ~ 한 프레임(약 16ms). 음수가 나오지 않게 0 이상으로 자른다.
        LastPressLatency = Math.Max(0.0, InputState.currentTime - ctx.time);
        OnPressed?.Invoke(lane);
    }
}
