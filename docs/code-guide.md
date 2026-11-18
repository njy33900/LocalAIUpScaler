# 코드 구조와 수정 기준

## 서식

- 스페이스 4칸으로 들여쓰기하고 논리적인 코드 블록 사이에는 빈 줄을 둡니다.
- 실행 구문 여러 개를 한 줄에 배치하지 않습니다. 조건·예외 처리와 콜백 본문도 줄을 나눕니다.
- XAML은 컨트롤과 속성을 줄바꿈하여 배치·바인딩·이벤트를 쉽게 비교합니다.
- C# 공백 규칙은 루트 `.editorconfig`를 따릅니다. `dotnet format whitespace LocalAIUpscaler.sln --no-restore`로 정리합니다.
- Python은 Black 25.1.0, 줄 길이 100을 기준으로 합니다. 서식 도구는 앱 실행 의존성에 포함하지 않습니다.
- 주석에는 취소 경계, 소유권, 동시성 제약과 설계 이유를 기록합니다. 코드가 이미 설명하는 단순 대입은 반복 설명하지 않습니다.

## 문서 주석

- C# 클래스·레코드·인터페이스·메서드는 XML 문서 주석으로 책임을 설명합니다. 요약은 `<summary>`에 쓰고, 호출 계약이 중요하면 `<param>`, `<returns>`, `<exception>`, `<remarks>`를 추가합니다.
- Python은 docstring을 사용합니다. 인자·반환·예외 설명이 필요하면 Google 스타일의 `Args:`, `Returns:`, `Raises:`를 사용합니다. 외부에서 가져온 코드의 라이선스와 기존 설명은 보존합니다.
- PowerShell 진입 스크립트에는 comment-based help의 `.SYNOPSIS`, `.DESCRIPTION`, 필요 시 `.PARAMETER`를 작성합니다.
- 주석은 메서드의 역할과 제약을 설명하고, 구현을 바꿀 때 함께 갱신합니다. 처리·검증·반환 사이에는 논리적인 구분에 맞춰 빈 줄을 둡니다.

## 책임과 데이터 흐름

| 위치 | 책임 |
|---|---|
| `src/Upscaler.Core/Domain.cs` | 공통 설정, 입력 검증, 파일 목록과 로그 |
| `src/Upscaler.Core/BatchRunner.cs` | 확정한 입력 목록의 순차 처리, 실패·취소 집계 |
| `src/Upscaler.PythonBridge/PythonWorker.cs` | 추론 프로세스 세션, JSONL 통신, 제한시간과 재시작 |
| `src/Upscaler.Wpf/MainViewModel.cs` | 탐색·정렬·선택, 프리뷰와 썸네일 요청, 제한된 메모리 캐시 |
| `src/Upscaler.Wpf/MainWindow.xaml.cs` | UI 이벤트, 배치·확대·패닝, 실행 확인과 모달 소유 관계 |
| `src/Upscaler.Wpf/Infrastructure/ThumbnailDecoder.cs` | 재사용 디코더 풀과 요청별 응답 격리 |
| `src/Upscaler.Wpf/ProgressWindow.cs` | 진행 상태, 취소 확인, 완료 후 결과 위치 열기 |
| `python/worker.py` | 모델 로딩, 타일 추론, OOM 축소 재시도와 결과 저장 |
| `python/imaging.py` | 포맷 확인, EXIF 방향, 비트 심도와 알파 변환 정책 |
| `python/decode.py` | 단발 프리뷰 디코딩 및 상주 썸네일 JSONL 서버 |

## 리팩토링 시 유지할 조건

1. UI 바인딩과 LRU 접근은 Dispatcher에서 처리하고 디스크 조회·디코딩은 백그라운드에서 수행합니다.
2. 탐색·정렬의 오래된 비동기 결과가 최신 화면을 덮지 않아야 합니다. 세대 번호와 취소 검사를 유지합니다.
3. 썸네일 컨테이너는 재활용됩니다. 이전 항목 요청을 취소하고 현재 요청인지 확인한 후 이미지를 할당합니다.
4. 추론 Worker의 대기 상태는 프로세스 세션별로 격리합니다. 종료한 세션의 응답이 새 작업에 전달되면 안 됩니다.
5. 취소는 응답 수신만으로 끝나지 않습니다. 프로세스 종료와 임시 파일 정리가 확인될 때까지 처리 창의 소유 창을 잠급니다.
6. 모델 출력은 임시 파일을 거쳐 덮어쓰기 없이 확정합니다. 입력 원본과 이미 완료된 결과를 보존합니다.
7. Python stdout은 통신 전용입니다. 로그나 디버그 출력으로 JSONL 또는 이미지 바이트 스트림을 오염시키지 않습니다.

변경 후 `scripts/test.ps1 -RenderUI -UIIntegration`으로 실제 추론·취소·프로세스 복구·UI 모달과 렌더링을 확인합니다. 기능 변경이 없는 서식 정리도 Python/C# 구문 의미와 XAML 바인딩이 유지되는지 확인해야 합니다.

## 빌드 출력

공통 `Directory.Build.props`에서 빌드 출력을 루트의 `out/Debug` 또는 `out/Release`로 모읍니다. 실행 파일은 각 구성 폴더의 `Upscaler.Wpf.exe`입니다. 프로젝트별 중간 산출물은 기존 `obj`에 격리합니다.
