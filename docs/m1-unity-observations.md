# M1 観察結果: Unity 6.3 LTS (6000.3.1f1, DX12)

ログ: `logs/unity-m1.log`（2026-10-16 取得、約 4 分間、Unity を起動してから受け入れテストの UI 操作を一通り実施）

## 出現したウィンドウ

すべて Unity.exe 本体（1 プロセス）に属するトップレベルウィンドウだった。他プロセスの窓は出ていない（IME は未検証）。

| クラス | タイトル | 用途（推定） | 備考 |
|---|---|---|---|
| UnityContainerWndClass | プロジェクト名 - … - Unity 6.3 LTS | メインエディタ | owner なし。起動直後のタイトルは 'Unity' |
| UnitySplashWindow | Opening project... | 起動スプラッシュ | popup layered、owner なし |
| UnityContainerWndClass | HDR Color | カラーピッカー | popup、owner=メイン |
| UnityContainerWndClass | UnityEditor.ObjectSelector → Select Volume Profile | オブジェクトピッカー | 生成後にタイトルが変わる |
| UnityContainerWndClass | '' → Preferences | Preferences | 生成後にタイトルが設定される |
| UnityContainerWndClass | '' → New MCP Connection | フローティングウィンドウ（MCP for Unity） | 同上 |
| UnityContainerWndClass | ''（約 122x29） | ツールチップ（推定） | |
| UnityContainerWndClass | Occlusion（150x39） | タブのドラッグ中のゴースト（推定） | |
| UnityPopupWndClass | UnityEditor.AddComponent.AddComponentWindow | Add Component | |
| UnityPopupWndClass | UnityEditor.Rendering.FilterWindow | 検索付きドロップダウン | |
| UnityPopupWndClass | UnityEditor.PopupWindow / '' | ツールバーのポップアップ | **13x13 で生成されてから拡大する** |
| #32768 | '' | ネイティブメニュー（右クリック、メニューバー、サブメニュー、Enum ドロップダウン） | popup topmost tool。owner がないものもある |

ポップアップ類はすべて `owner=メインウィンドウ` か `#32768`。

## タイミング

- イベントを受けてからスナップショットに反映されるまでの時間: 平均 1.3ms、ほぼすべて 2ms 未満（426 回）。トラッカー側は十分速い
- 寿命が最も短かったウィンドウ: UnityPopupWndClass が 348ms、#32768 メニューが 721ms。WGC の開始に数十 ms かかると、表示時間のうち 1〜2 割を取り逃す計算になる → M4 で実測する
- ウィンドウの出現と消滅はすべてイベント経由で検知できた。定期再列挙で初めて拾えたのは「タイトル変更」と「スタイル変更（#32768 の layered フラグの付け外しなど）」だけで、ウィンドウの取りこぼしはなかった

## 設計への反映

1. **タイトル変更を即時に検知する** → `EVENT_OBJECT_NAMECHANGE` を購読するよう修正済み。Unity はウィンドウ生成後にタイトルを設定するので、タイトルで除外するルールが最大 500ms 遅れて効く状態は配信事故につながる
2. **生成直後のサイズ変化への対応が必要**（PopupWindow は 13x13 → 125x70 → 356x133 と大きくなる）。CaptureManager では、フレームのサイズが変わったら `FramePool.Recreate` すること
3. **メニューのフェードイン**: #32768 は最初 layered で出て、あとからフラグが外れる。WGC で半透明のフレームを正しく扱えるか M4 で確認する
4. **フィルタはプロセス単位で十分**: Unity 本体以外の Unity.exe（AssetImportWorker など）は表示ウィンドウを持たない。スプラッシュも Unity の窓として映ってよい
5. **起動時の検出遅延**: プロセスを 2 秒ごとにポーリングしているため、Unity の起動を検知するまで最大 2 秒かかる（配信中に支障はない）

## 未確認

- Project Settings、Enum ドロップダウン、ツールチップは、それぞれどのウィンドウだったかを確定できていない（タイトルが空のもの・#32768 のどれかと推定）
- IME の候補窓
- UI Toolkit 製のドロップダウン（`GenericDropdownMenu`）が独立したウィンドウになるか
