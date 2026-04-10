# Portable Obsidian Web Bridge

로컬 옵시디언 볼트(Vault)를 웹으로 중계하는 실행 파일 기반 서버. 별도 설정 없이 외부 접속 주소(Cloudflare Tunnel) 자동 생성 지원.

## 주요 기능
- **실시간 파일 시스템 연동**: 볼트 내 `.md` 파일 및 이미지 실시간 읽기/쓰기.
- **옵시디언 호환 렌더링**: 위키링크 이미지(`![[filename]]`), 실시간 미리보기(Preview) 지원.
- **사이드바 관리**: 폴더 접기/펼치기, 너비 조절(Resize), 전체 토글(≡) 지원.
- **파일 관리**: 웹에서 파일 생성, 폴더 생성, 이름 변경, 폴더 이동(📦) 가능.
- **실시간 동기화**: 로컬 수정 시 웹 즉시 반영 (FileSystemWatcher), 웹 수정 시 로컬 즉시 저장.
- **보안 모드**: 콘솔 명령어로 읽기 전용(ReadOnly) 모드 실시간 전환.

## 콘솔 명령어 (Server Console)
- `ro` : 읽기 전용 모드로 전환.
- `rw` : 편집 가능 모드로 전환.
- `path [경로]` : 옵시디언 볼트 경로 실시간 변경 및 설정 저장.
- `url` : 현재 접속 가능한 내부/외부 주소 표시.
- `help` : 명령어 도움말.

## 빌드 및 실행
### 빌드 (Windows PowerShell)
```powershell
./build_portable.ps1
```
- 결과물: `PortableBuild/Windows/`, `PortableBuild/Linux/`

### 실행
- **Windows**: `PortableObsidian.exe` 실행.
- **Linux**: `chmod +x PortableObsidian && ./PortableObsidian`

## 설정 (config.json)
프로그램 최초 실행 시 생성됨.
- `Port`: 웹 서버 포트 (기본 30331).
- `VaultPath`: 대상 옵시디언 볼트 절대 경로.
- `IsReadOnly`: 시작 시 읽기 전용 여부.
- `TunnelToken`: Cloudflare 고정 도메인 사용 시 토큰 입력 (비워두면 랜덤 주소).

## 기술 스택
- **Framework**: Blazor WebAssembly (.NET 8), ASP.NET Core SignalR.
- **Markdown**: Markdig (Advanced Extensions, SoftlineBreak).
- **Process**: CliWrap (cloudflared 관리).
