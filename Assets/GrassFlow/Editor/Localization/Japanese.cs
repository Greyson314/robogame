using System.Collections.Generic;

namespace GrassFlow.Localization {
    public class Japanese : Locale {

        new public static string GetTooltip(string memberName) {
            _tooltips.TryGetValue(memberName, out var tip);
            return tip;
        }

        new public static string GetMatTooltip(string memberName) {
            _materialTooltips.TryGetValue(memberName, out var tip);
            return tip;
        }

        new public static string GetName(string memberName) {
            _names.TryGetValue(memberName, out var name);
            return name;
        }

        new public static string GetMsg(string memberName) {
            _messages.TryGetValue(memberName, out var msg);
            return msg;
        }

        internal new static string isoCodeStatic => "ja";
        internal override string isoCode => isoCodeStatic;
        internal override Dictionary<string, string> names => _names;
        internal override Dictionary<string, string> tooltips => _tooltips;
        internal override Dictionary<string, string> messages => _messages;
        internal override Dictionary<string, string> materialTooltips => _materialTooltips;


        static readonly Dictionary<string, string> _names = new Dictionary<string, string> {
            { nameof(GrassMesh.asyncInitialization), "非同期初期化"},
            { nameof(GrassMesh.receiveShadows), "影を受ける"},
            { nameof(GrassMesh.castShadows), "影を落とす"},
            { nameof(GrassMesh.renderingLayer), "レンダリングレイヤーマスク"},
            { nameof(GrassMesh.instanceCount), "インスタンス数"},
            { nameof(GrassMesh.lodSteps), "LODステップ"},
            { nameof(GrassMesh.renderType), "レンダータイプ"},
            { nameof(GrassMesh.grassMesh), "地形メッシュ"},
            { nameof(GrassMesh.customMeshLods), "草のLOD"},
            { nameof(GrassMesh.crossFade), "クロスフェード"},
            { nameof(GrassMesh.crossFadeRange), "クロスフェード範囲"},
            { nameof(GrassMesh.customGrassMesh), "カスタムグラスメッシュ"},
            { nameof(GrassMesh.mainGrassMat), "メインの草マテリアル"},
            { nameof(GrassMesh.terrainObject), "地形オブジェクト"},
            { nameof(GrassMesh.terrainTransform), "地形トランスフォーム"},
            { nameof(GrassMesh.colorMap), "カラーマップ"},
            { nameof(GrassMesh.paramMap), "パラメータマップ"},
            { nameof(GrassMesh.typeMap), "タイプマップ"},
            { nameof(GrassMesh.grassPerTri), "インスタンスあたりの草"},
            { nameof(GrassMesh.terrainGrassDensity), "地形の草の密度"},
            { nameof(GrassMesh.normalizeMaxRatio), "正規化の最大比率"},
            { nameof(GrassMesh.terrainSlopeThresh), "地形の傾斜しきい値"},
            { nameof(GrassMesh.terrainSlopeFade), "地形の傾斜フェード"},
            { nameof(GrassMesh.lodParams), "LODパラメータ"},
            { nameof(GrassMesh.maxRenderDist), "最大レンダー距離"},
            { nameof(GrassMesh.expandBounds), "草の高さで境界を拡大"},
            { nameof(GrassMesh.frustumCull), "フラスタムカリング"},
            { nameof(GrassMesh.frustumCullThresh), "フラスタムカリングのしきい値"},
            { nameof(GrassMesh.bakeDensity), "密度のベイク"},
            { nameof(GrassMesh.bakeData), "データのベイク"},

            { nameof(GrassFlowRenderer.visualizeChunkBounds), "チャンクの境界を視覚化"},
            { nameof(GrassFlowRenderer.renderLayer), "レンダーレイヤー"},
            { nameof(GrassFlowRenderer.updateBuffers), "バッファの更新"},
            { nameof(GrassFlowRenderer.useMaterialInstance), "マテリアルインスタンスの使用"},
            { nameof(GrassFlowRenderer.terrainExpansion), "地形の拡大"},
            { nameof(GrassFlowRenderer.normalizeMeshDensity), "メッシュ密度の正規化"},

            
			//--------------------------------------------------------------------------------
			//インスペクター関連---------------------------------------------------------------
			//--------------------------------------------------------------------------------			
            { "refreshButton", "更新" },
            { "settingsTab", "設定" },
            { "paintModeTab", "ペイントモード" },
            { "globalRendererSettings", "グローバルレンダラー設定" },
            { "grassRenderProperties", "草のレンダープロパティ" },
            { "terrainHeader", "地形" },
            { "maxRatio", "最大比率" },
            { "lodHeader", "LOD" },
            { "meshLodChunks", "メッシュLODチャンク" },
            { "terrainLodChunks", "地形LODチャンク" },
            { "grassMeshesHeader", "草メッシュ" },
            { "addCopyGrassMesh", "草メッシュの追加/コピー" },
            { "addFromSelected", "選択から追加" },
            { "detailMapsHeader", "ディティールマップ" },
            { "revertChangesButton", "変更を元に戻す" },
            { "bakeDensityToMeshButton", "密度をメッシュにベイク" },
            { "settingsHeader", "設定" },
            { "brushColor", "ブラシの色" },
            { "useBrushOpacity", "ブラシの不透明度を使用" },
            { "grassTypeIndex", "草タイプのインデックス" },
            { "clampRange", "クランプ範囲" },
            { "brushSize", "ブラシサイズ" },
            { "brushStrength", "ブラシの強度" },
            { "raycastLayerMask", "レイキャストレイヤーマスク" },
            { "paintContinuously", "連続してペイント" },
            { "useDeltaTimePaint", "デルタタイムペイントを使用" },
            { "splatMapsHeader", "スプラットマップ" },
            { "splatLayer", "スプラットレイヤー" },
            { "tolerance", "許容範囲" },
            { "applyAdditive", "加算を適用" },
            { "applySubtractive", "減算を適用" },
            { "applyReplace", "置換を適用" },
            { "paintGrassColor", "草の色をペイント" },
            { "paintGrassDensity", "草の密度をペイント" },
            { "paintGrassHeight", "草の高さをペイント" },
            { "paintGrassFlatness", "草の平坦度をペイント" },
            { "paintGrassWindStrength", "草の風の強さをペイント" },
            { "paintGrassType", "草のタイプをペイント" },
            { "brushesHeader", "ブラシ" },
            { "noBrushesDefined", "ブラシが定義されていません。" },

            
			//--------------------------------------------------------------------------------
			//マテリアル関連---------------------------------------------------------------
			//--------------------------------------------------------------------------------
			// 草のプロパティ
            { "_Color", "草の色" },
            { "bladeHeight", "ブレードの高さ" },
            { "bladeWidth", "ブレードの幅" },
            { "flatTint", "平坦度の色合い" },
            { "altCol", "バリエーションカラー" },
            { "DISABLE_DECALS", "デカールを無効にする" },
            { "_ReceiveShadows", "影を受ける" },
            { "specTint", "スペキュラの色合い" },
            { "_Metallic", "メタリック" },
            { "_Gloss", "光沢" },
            { "_noiseScale2", "セカンダリノイズスケール" },
            { "_noiseSpeed2", "セカンダリノイズ速度" },
            { "occMult", "オクルージョン強度" },
            { "bladeOffset", "ブレードオフセット" },
            { "bladeSharp", "ブレードの鋭さ" },
            { "seekSun", "太陽を求める" },
            { "topViewPush", "トップビュー調整" },
            { "flatnessMult", "平坦度調整" },
            { "_BILLBOARD", "ビルボード" },
            { "variance", "分散 (p,h,c,w)" },

            // ライティング
            { "_ppLights", "ピクセル単位のライト" },
            { "_AO", "AO" },
            { "ambientCO", "アンビエント" },
            { "ambientCOShadow", "影のアンビエント" },
            { "edgeLight", "エッジライト" },
            { "edgeLightSharp", "エッジライトの鋭さ" },
            { "blendNormal", "サーフェス法線のブレンド" },
            { "_GF_SPECULAR", "スペキュラを有効にする" },
            { "specSmooth", "滑らかさ" },
            { "specularMult", "スペキュラの乗数" },
            { "specHeight", "スペキュラの高さ調整" },
            { "_GF_NORMAL_MAP", "ノーマルマッピングを有効にする" },
            { "normalStrength", "強度" },
            { "bumpMap", "ノーマルマップ" },

            // セルフシャドウ
            { "GF_SELF_SHADOW", "フェイクセルフシャドウ" },
            { "selfShadowWind", "セルフシャドウの風" },
            { "selfShadowScaleOffset", "セルフシャドウのスケール/オフセット" },

            // LOD
            { "_ALPHA_TO_MASK", "アルファをマスクに" },
            { "widthLODscale", "幅のLODスケール" },
            { "_GF_USE_DITHER", "ディザを使用" },
            { "grassFade", "草のフェード" },
            { "grassFadeSharpness", "フェードの鋭さ" },
            { "_LOD_SCALING", "LODスケーリングを使用" },

            // 風
            { "windMult", "風の強さの乗数" },
            { "windTint", "風の色合い" },
            { "_noiseScale", "ノイズスケール" },
            { "_noiseSpeed", "ノイズ速度" },
            { "windDir", "風向" },
            { "windDir2", "第二の風向" },

            // 曲げ
            { "_MULTI_SEGMENT", "マルチセグメント" },
            { "bladeLateralCurve", "曲率" },
            { "bladeVerticalCurve", "垂下" },
            { "bladeStiffness", "柔軟性" },

            // マップとテクスチャリング
            { "_SEMI_TRANSPARENT", "アルファクリップを有効にする" },
            { "alphaLock", "テクスチャのアルファを破棄" },
            { "alphaMult", "アルファ乗数" },
            { "alphaClip", "アルファクリップ" },
            { "numTextures", "テクスチャ数" },
            { "textureAtlasScalingCutoff", "タイプテクスチャのスケーリングカットオフ" },
            { "_SpecMap", "スペキュラマップ" },
            { "_OccMap", "オクルージョンマップ" },
            { "_MainTex", "草テクスチャ" },
            { "dhfParamMap", "パラメータマップ" },

            // 最適化
            { "_Cull", "カリングモード" },
            { "MESH_COLORS", "頂点の高さの色を使用" },
            { "MESH_NORMALS", "メッシュ法線を使用" },
            { "MESH_UVS", "メッシュUVを使用" },
            { "MAP_COLOR", "動的カラーマップ" },
            { "MAP_PARAM", "動的パラメータマップ" },
            { "MAP_TYPE", "動的タイプマップ" },
            { "GRASS_RIPPLES", "波紋を許可" },
            { "GRASS_FORCES", "複数の力を許可" },
        };

        static readonly Dictionary<string, string> _tooltips = new Dictionary<string, string> {
            { nameof(GrassMesh.asyncInitialization), "大きなメッシュがある場合にロード時間をわずかに短縮できる、初期処理の部分的にマルチスレッド化された非同期実行を有効にします。これの欠点は、草がロードされる前にゲームが開始される可能性があることです。"},
            { nameof(GrassMesh.receiveShadows), "草に影を受けます。特にカスケードシャドウがオンの場合は高価になる可能性があります。（適切にレンダリングするには、深度パス付きの草シェーダーが必要です）"},
            { nameof(GrassMesh.castShadows), "草が影を落とします。かなり高価なオプションです。（レンダリングするには、深度パス付きの草シェーダーも必要です）"},
            { nameof(GrassMesh.renderingLayer), "URP/HDRPレンダリングレイヤーマスク。"},
            { nameof(GrassMesh.instanceCount), "この値は、「インスタンスごとの草」と、基礎となるソースジオメトリの三角形の数で因数分解され、レンダリングできるインスタンスの総数を計算します。"},
            { nameof(GrassMesh.lodSteps), "LODシステムによって草の密度が減少し、遠くのインスタンスの数を減らすことができるステップ数。\n重要：この設定は表示できる草の最小量も制御するため、十分に高く設定してください。つまり、この設定が高いほど、遠くに表示できる草の量が少なくなります。"},
            { nameof(GrassMesh.renderType), "この草のモード。メッシュは草をメッシュの三角形にアタッチし、地形は草をUnityの地形オブジェクトの表面にアタッチします。"},
            { nameof(GrassMesh.grassMesh), "メッシュモードで草をアタッチするメッシュ。"},
            { nameof(GrassMesh.customMeshLods), "草をレンダリングするときに使用するメッシュ、マテリアル、および距離。個々のLODは、フラスタムカリングを使用しない場合にのみ許可されます。"},
            { nameof(GrassMesh.crossFade), "有効にすると、LODはいくつかのオーバーラップで描画され、スムーズな移行のためにフェードされます。\nオーバーラップフェード範囲中にチャンクが2回描画され、現在フェードしているチャンクはバッチ処理できないため、パフォーマンスに影響があります。\n注：LODに使用されるマテリアルでディザリングを有効にする必要があります。"},
            { nameof(GrassMesh.crossFadeRange), "LOD間でクロスフェードが発生する範囲"},
            { nameof(GrassMesh.customGrassMesh), "草のレンダリングに使用されるメッシュ。"},
            { nameof(GrassMesh.mainGrassMat), "草のレンダリングに使用されるマテリアル。GrassFlowシェーダーのいずれかを使用する必要があります。"},
            { nameof(GrassMesh.terrainObject), "地形モードで草をアタッチする地形オブジェクト。"},
            { nameof(GrassMesh.terrainTransform), "草が属するトランスフォーム。"},
            { nameof(GrassMesh.colorMap), "草の色を制御するテクスチャ。このテクスチャのアルファチャンネルは、色の適用方法を制御するために使用されます。アルファが1の場合、色はマテリアルの色でも乗算されます。0の場合、マテリアルの色は無視されます。中間値も機能します。"},
            { nameof(GrassMesh.paramMap), "草のさまざまなパラメータを制御するテクスチャ。赤チャンネル=密度。緑チャンネル=高さ、青チャンネル=平坦度。アルファチャンネル=風の強さ。"},
            { nameof(GrassMesh.typeMap), "草テクスチャアトラス（使用している場合）から使用するテクスチャを制御するテクスチャ。注：このテクスチャの仕組みについては、ドキュメントをお読みください。"},
            { nameof(GrassMesh.grassPerTri), "不明な場合は1のままにしてください。\nレンダリングされたメッシュジオメトリを複製する回数。基本的に、より多くのジオメトリがインスタンス化されるのではなく、「本物」になるようにします。\n本物のジオメトリとインスタンス化されたジオメトリの最速のしきい値があるので、自分にとって何が良いかを確認するために試してみる必要があります。\n注：モバイルで役立つ場合と役立たない場合があります。\n注：これを高く設定しすぎると、奇妙なLODポッピングが発生する可能性があり、フラスタムカリングの場合は1に設定する必要があります。"},
            { nameof(GrassMesh.terrainGrassDensity), "地形モードでレンダリングする草の基本レベル。この量はインスタンス数で乗算され、LODフォールオフを制御します。"},
            { nameof(GrassMesh.normalizeMaxRatio), "最大の三角形を細分化できる最大比率。基本的には、メッシュを正規化しようとするときの細分化密度を制御します。良い結果が得られる限り、これをできるだけ低く設定することをお勧めします。"},
            { nameof(GrassMesh.terrainSlopeThresh), "-1から1の角度しきい値。地形に草をスポーンするための上方向（0、1、0）との比較。"},
            { nameof(GrassMesh.terrainSlopeFade), "草が「フェードアウト」するようにスケーリングされる地形の傾斜しきい値からの距離。"},
            { nameof(GrassMesh.lodParams), "草のLODパラメータを制御します。X = レンダリング距離。Y = 密度フォールオフの鋭さ（草の量がゼロに減少する速さ）。Z = オフセット。基本的には、この距離内でブレードが飛び出すのを防ぐ正の数。"},
            { nameof(GrassMesh.maxRenderDist), "草チャンクの最大レンダリング距離を制御します。この値は、主に遠くのチャンクをレンダリングからすばやく除外するために使用されます。"},
            { nameof(GrassMesh.expandBounds), "球体など、上を向いていないメッシュに草をレンダリングする場合に便利です。草はあらゆる方向を向く可能性があるため、チャンクの境界を草の最大潜在的な高さまで拡大する必要があります。\nしかし、ほとんどの地形には上向きの草しかないため、あらゆる方向に境界を拡大しない方が最適な場合があります。\nこれが無効になっている場合でも、境界は垂直方向に拡大されることに注意してください。"},
            { nameof(GrassMesh.frustumCull), "草のフラスタムカリング（カメラビューの外側の草を破棄する）を使用するかどうか。追加のVRAMを使用します。一般的に、これは高いチャンク数をレンダリングしない限りパフォーマンスにはあまり役立ちません。また、これを使用すると、カメラのビューの外側の草がカメラのビューに影を落とすのが簡単であるため、影に問題が発生する可能性があります。\nこれがオフの場合、Unityはチャンクごとにカリングを処理し、各スレッドを個別にレンダリングします。\nこれがオンの場合、コンピュートシェーダーは各草インスタンスを手動でカリングし、1つの描画呼び出しが発行されてすべてを一度にレンダリングします。"},
            { nameof(GrassMesh.frustumCullThresh), "水平および垂直ビューのしきい値。カメラのビューの外側で草がどれだけ離れているかを判断してカリングします。一般的に、これらはビューの端で草が飛び出すのを見ることができない限り、できるだけ低く設定する必要があります。"},
            { nameof(GrassMesh.bakeDensity), "パラメータマップを使用する場合、これは密度チャンネルに基づいて草のみを生成します。これははるかに効率的ですが、唯一の注意点は、実行時に草の密度を動的にペイントできないことです。"},
            { nameof(GrassMesh.bakeData), "メモリコストが増加しますが（37％）、カラー/パラメータ/タイプマップを使用する場合により効率的です。\nこれを無効にしてマップを使用する場合は、マテリアルでそれらのマップの動的マップ設定を有効にする必要があります。\n重要：フラスタムカリングは現在、この設定がオフの場合と互換性がありません。"},

            { nameof(GrassFlowRenderer.visualizeChunkBounds), "これにツールチップは本当に必要ですか？ええと、まあ、チャンクの境界は、奇妙な角度でカリングされたときに草が飛び出すのを避けるために、ブレードの高さによって自動的に拡大されます。"},
            { nameof(GrassFlowRenderer.renderLayer), "草をレンダリングするレイヤー。"},
            { nameof(GrassFlowRenderer.updateBuffers), "この設定はエディタにのみ影響します。スクリプトが再コンパイルされたときなどの視覚的なポッピングを防ぐため、ほとんどの場合、これをオンにしておくとよいでしょう。" +
            "ゲームのパフォーマンスをより正確に表示するためにオフにすることもできますが、実際にはほとんど違いはありません。"},
            { nameof(GrassFlowRenderer.useMaterialInstance), "trueの場合、レンダリング用のマテリアルのインスタンスが作成されます。複数の草に同じマテリアルを使用し、テクスチャなどを変えたい場合に重要です。"},
            { nameof(GrassFlowRenderer.terrainExpansion), "地形で草チャンクを拡大する量。チャンクの端のアーティファクトを回避するのに役立ちます。見栄えが悪くならない限り、これをできるだけ低く設定することが望ましいです。"},
            { nameof(GrassFlowRenderer.normalizeMeshDensity), "ソースメッシュの密度が非常に不均一でない限り、この設定を有効にしないでください。処理時間が増加し、おそらくより悪い結果になります。" +
            "この設定は、すべての三角形をできるだけ同じサイズにするためにメッシュを細分化しようとします。元の形状は正確に一致します。" +
            "これによりメッシュが細分化されるため、増加した密度を考慮してGrassPerTriを減らすことをお勧めします。"},

            
			//--------------------------------------------------------------------------------
			//インスペクター関連---------------------------------------------------------------
			//--------------------------------------------------------------------------------
            { "refreshButton", "現在のすべてのデータを解放/破棄し、すべてをリセットします。特定のものを変更した後に草をリセットするために使用します。" },
            { "globalRendererSettings", "これらの設定はすべての草メッシュで共有されます" },
            { "meshLodChunks", "LODカリングに使用するチャンクの数。各チャンクまでの距離は、そこにレンダリングされる草の量を制御します。メッシュモードでは、通常、Y方向に1つ以上のチャンクは必要ありませんが、非常に垂直な地形がある場合は役立つ場合があります。チャンクが多すぎるとパフォーマンスが低下しますが、チャンクが少なすぎると、草をカリングするときに見栄えが悪くブロック状になります。そのため、見栄えが悪くならないように、できるだけ少ないチャンクを設定してください。（ヒント：思ったほど多くは必要ありません。）" },
            { "terrainLodChunks", "LODカリングに使用するチャンクの数。各チャンクまでの距離は、そこにレンダリングされる草の量を制御します。チャンクが多すぎるとパフォーマンスが低下しますが、チャンクが少なすぎると、草をカリングするときに見栄えが悪くブロック状になります。そのため、見栄えが悪くならないように、できるだけ少ないチャンクを設定してください。（ヒント：思ったほど多くは必要ありません。）" },
            { "addCopyGrassMesh", "別のメッシュまたは地形で草をレンダリングできる追加のGrassMeshを追加し、現在選択されているGrassMeshから設定をコピーします。" },
            { "addFromSelected", "階層で選択したオブジェクトから追加のGrassMeshを追加しようとします。トランスフォームとメッシュを自動的に入力します。これを行うには、インスペクターをロックしてからオブジェクトを選択する必要がある場合があります。" },
            { "revertChangesButton", "最後に保存されてからディティールマップに加えられた変更を破棄します。マップは、Ctrl + Sなどでプロジェクトアセットが保存されるたびに保存されます。元に戻すホットキー：Shift-R。このアクションは「元に戻す/やり直し」をサポートしているはずですが、おそらく機能します。" },
            { "bakeDensityToMeshButton", "パラメータマップの密度情報に基づいて新しいメッシュを作成します。このメッシュを使用して、メッシュの特定の部分にのみ草をより効率的にレンダリングできます。結果のメッシュを自動的に適用しません。" },
            { "useBrushOpacity", "ペイント時にブラシの不透明度を使用するかどうか。草の種類を最大の強度でペイントする場合、ブラシの不透明度が密度に望ましくない影響を与えるアーティファクトを回避するために、これをオフにすることが理想的です。" },
            { "grassTypeIndex", "草テクスチャアトラスへのインデックス。ペイントするテクスチャを選択するため。" },
            { "clampRange", "ペイント中のパラメータの最小範囲と最大範囲。これは、加算的または減算的ではなく、基本的に設定値をペイントするために使用できます。" },
            { "raycastLayerMask", "このマスクは、ペイントのために地形/メッシュをレイキャストするときに使用されます。これを使用して、地形があるレイヤーにのみペイントし、ブロックしているオブジェクトを通り抜けてペイントしたり、その逆を行ったりすることができます。" },
            { "paintContinuously", "オフの場合、マウスを動かしてペイントする必要があります。それ以外の場合は、マウスが押されている間、連続してペイントします。" },
            { "useDeltaTimePaint", "オンの場合、ブラシの強度はデルタタイムで乗算され、ペイントの強度がフレームレートに依存しなくなります。ブラシをスタンプのように使用し、強度1を使用して1回のクリックでブラシ全体を草に適用したい場合にオフにすると便利です。" },
            { "splatLayer", "草が表示される場所をマスクするために使用するスプラットテクスチャレイヤーのインデックス。" },
            { "tolerance", "スプラットマップレイヤーを適用するときの不透明度の許容範囲を制御します。" },
            { "applyAdditive", "選択したレイヤーに基づいて草を追加しますが、既存の草は削除しません。" },
            { "applySubtractive", "選択したレイヤーに基づいて草を削除しますが、スプラットマップの外側の草には影響しません。" },
            { "applyReplace", "選択したレイヤーに基づいて草を追加し、既存の草を削除して上書きします。" },
            { "paintGrassColor", "クリックして色をペイントします。簡単です。" },
            { "paintGrassDensity", "クリックして草を塗りつぶします。Shiftキーを押しながらクリックすると草を消去します。" },
            { "paintGrassHeight", "クリックして草を高くします。Shiftキーを押しながらクリックすると草を低くします。" },
            { "paintGrassFlatness", "クリックして草を平らにします。Shiftキーを押しながらクリックすると平らでなくします。" },
            { "paintGrassWindStrength", "クリックして風の強さを上げます。Shiftキーを押しながらクリックすると下げます。" },
            { "paintGrassType", "クリックして、草テクスチャアトラス（使用している場合）から表示されるテクスチャをペイントします。Shiftキーを押しながらクリックすると、最初のテクスチャをペイントします。ブラシの強度は、選択したタイプの密度を制御します。" },
            { "paintToolColor", "色" },
            { "paintToolDensity", "密度" },
            { "paintToolHeight", "高さ" },
            { "paintToolFlatness", "平坦度" },
            { "paintToolWind", "風の強さ" },
            { "paintToolType", "草の種類" },
        };

        static Dictionary<string, string> _materialTooltips = new Dictionary<string, string>() {

            { "_Color", "草の刃の基本色。" },
            { "bladeHeight", "草の刃の全体の高さ。" },
            { "bladeWidth", "草の刃の全体の幅。" },
            { "flatTint", "平らになった草に適用される色合い。" },
            { "altCol", "バリエーションに使用される代替色。" },
            { "DISABLE_DECALS", "草のデカールを無効にします。" },
            { "_ReceiveShadows", "草が影を受けるかどうか。" },
            { "specTint", "スペキュラハイライトの色合い。" },
            { "_Metallic", "草のメタリックな外観を制御します。" },
            { "_Gloss", "スペキュラ反射のための草の光沢を制御します。" },
            { "_noiseScale2", "セカンダリ風ノイズのスケール。" },
            { "_noiseSpeed2", "セカンダリ風ノイズの速度。" },
            { "occMult", "オクルージョンマップの強度。" },

            {"bladeOffset", "地形上の草の位置に高さオフセットを追加します。微調整に役立ちます。"},
            {"bladeSharp", "草のブレードの鋭さを制御します。0は完全な点、1は長方形です。"},
            {"seekSun", "草が表面法線にどれだけ整列するかを制御します。0は完全に整列し、1は上を向きます。"},
            {"topViewPush", "上から見たときに草にわずかなオフセットを追加しようとします。これにより、下を見たときにより多くの深さと密度を与えるのに役立ちます。"},
            {"flatnessMult", "パラメータマップの平坦度チャンネルを使用するときに、草がどれだけ「平ら」に押されるかを制御します。"},
            {"_BILLBOARD", "草が常にカメラの方を向くかどうか。"},
            {"variance", "これらの4つの値は、草が特定の方法でどれだけランダム化されるかを制御します。値は次のとおりです。X = 位置、Y = 高さ、Z = 色、W = 幅"},

            //ライティング
            {"_ppLights","ピクセルごとにシェーディングを計算します。わずかに遅くなりますが、カスタムの草メッシュを使用する場合にのみ顕著であり、ノーマルマッピングに必要です。"},
            {"_AO", "草のブレードの下部がどれだけ暗いかを制御します。0は暗く、1は暗さなしです。"},
            {"ambientCO", "シェーディングがどれだけ暗くなるかを制御します。"},
            {"ambientCOShadow", "光源の影の強さの設定に加えて、これにより、受け取った影の強さをさらに調整できます。"},
            {"edgeLight", "光の方向が草のブレードに対してエッジオンの場合に追加される明るさの強さを制御します。"},
            {"edgeLightSharp", "追加されたエッジオンライトの明るさの鋭さを制御します。"},
            {"blendNormal", "メッシュ法線を地形表面法線とブレンドします。これにより、シェーディングとスペキュラをより適切に制御できます。"},
            {"_GF_SPECULAR", "スペキュラハイライトを有効にします。最悪の場合、約0.1ミリ秒のわずかなパフォーマンスコストが追加されます。"},
            {"specSmooth", "スペキュラハイライト/反射の表面の滑らかさ/ぼやけを制御します。"},
            {"specularMult", "スペキュラハイライトの強度の乗数。"},
            {"specHeight", "スペキュラ反射の高さ調整。草の根元にスペキュラハイライトがないように調整するために使用できます。"},
            {"_GF_NORMAL_MAP", "ノーマルマッピングを有効にします。中程度のパフォーマンスコストがあり、最悪の場合約1ミリ秒、妥当な場合は0.1ミリ秒です。"},
            {"normalStrength", "ノーマルマッピング効果の強度。"},
            {"bumpMap", "ノーマルマッピングに使用するテクスチャ。"},

            //セルフシャドウ
            {"GF_SELF_SHADOW", "実際に影をレンダリングせずに草に偽の影を追加する安価な手法を有効にします。これは基本的に、メインライトの視点から草のテクスチャを草に再投影することで機能します。\n" +
                "草メッシュの頂点がx / z軸で-0.5から0.5、y軸で0から1であると想定します。\n" +
                "切り抜きテクスチャと草カードで最も見栄えが良くなります。"},
            {"selfShadowWind", "セルフシャドウが風によってどれだけ変調されて動きを与えるか。"},
            {"selfShadowScaleOffset", "（x、y）：影の投影に適用されるスケール。\n（z、w）：影の投影に適用されるオフセット。\nこれらを微調整して、特定のメッシュの配置を微調整できます。"},


            //LOD
            {"_ALPHA_TO_MASK", "有効にすると、シェーダーでAlphaToMaskがオンになります。そして、これのパフォーマンスは非常に複雑です。有効にすると草の見栄えが良くなる場合とそうでない場合があります。"},
            {"widthLODscale", "カメラからの距離が増加するにつれてブレードの幅がどのように成長するかを制御します。これにより、あまり目立たないように、同じ領域をより少ない草で覆うことができます。"},
            {"_GF_USE_DITHER", "カメラまでの特定の距離内、または遅延モードで常にLOD遷移をさらに隠すために草をディザリングします。" +
                "ほとんどの場合、これをオンにすると見栄えが良くなりますが、望ましくない可能性のあるいくつかのアーティファクトが発生します。" +
                "LOD遷移で特に悪いポッピングに気付かない限り、これをオフのままにしてください"},
            {"grassFade", "草が視覚的にフェードする距離。注：これはLOD設定を制御しません。これらはGrassFlowコンポーネントとは別に設定する必要があり、この設定は視覚的なものです。"},
            {"grassFadeSharpness", "草のフェードの鋭さ。"},
            {"_LOD_SCALING", "LODフェードインのために草を垂直にスケーリングします。"},

            //風
            {"windMult", "全体的な風の強さの乗数。"},
            {"windTint", "風が強く影響するときに草が色付けされる色。アルファは強度を制御します。"},
            {"_noiseScale", "風のノイズサンプリングのスケール。風の突風のサイズをある程度制御します。"},
            {"_noiseSpeed", "ノイズが草の上をどれだけ速くスクロールして風のパターンを変更するか。風速のように機能しますが、一致するように風の強さを調整する必要があります。"},
            {"windDir  ", "風が吹く方向。これらの値のサイズが基本的に強度を決定します。"},
            {"windDir2", "風向と同じですが、第二の風向を制御します。常に一方向に吹くのではなく、風に多様性を与えるのに役立ちます。"},
            
            //曲げ
            {"_MULTI_SEGMENT", "各草のブレードに余分なセグメントを追加し、風または曲率から曲がることを可能にします。" +
                "セグメントの最小数と最大数は、GrassFlow/Shaders/GrassStructsVars.cgincファイルの先頭の数を調整することで変更できます。" +
                "LOD設定に基づいて、草のセグメントの数は距離とともに減少します。"},
            {"bladeLateralCurve", "草が持つ自然な曲がりの量。"},
            {"bladeVerticalCurve", "草を表面に向かって引き下げます。"},
            {"bladeStiffness", "風/波紋に反応して草がどれだけ曲がるかを制御します。"},

            //マップとテクスチャリング
            {"_SEMI_TRANSPARENT", "アルファ付きのテクスチャの使用を有効にします。"},
            {"alphaLock", "アルファクリッピングを適用しながら、草のテクスチャ自体のアルファを破棄します。テクスチャのアルファが悪い場合や、使用したくない場合に役立ちます。"},
            {"alphaMult", "テクスチャのアルファの乗数。これを増やすと、テクスチャのアルファが十分にシャープでない場合に微調整できます。"},
            {"alphaClip", "透明なテクスチャのクリッピングの感度を制御します。"},
            {"numTextures", "タイプマップテクスチャアトラスのテクスチャの数にこれを設定します。タイプマップを使用する場合にのみ使用されます。"},
            {"textureAtlasScalingCutoff", "LOD幅のスケーリングがオフになるタイプマップのテクスチャインデックス。たとえば、3に設定すると、スケーリングはアトラスの最初の3つのテクスチャにのみ適用されます。" +
                "タイプマップを使用する場合にのみ使用されます。"},
            {"_SpecMap", "遅延レンダリング用のスペキュラマップ。"},
            {"_OccMap", "遅延レンダリング用のオクルージョンマップ。"},
            {"_MainTex", "草のブレード/四角形を詳細に表示するために使用されるテクスチャ。これはアルファクリップに使用されるテクスチャです。タイプマップと組み合わせて使用​​される水平テクスチャアトラスにすることができます。その場合は、テクスチャの数のプロパティも設定してください。"},
            {"colorMap", "GrassFlowのカラーマップ。通常、これはGrassFlowRendererによって設定されます。何をしているのかわからない限り、触れないでください。"},
            {"dhfParamMap", "GrassFlowのパラメータマップ。通常、これはGrassFlowRendererによって設定されます。何をしているのかわからない限り、触れないでください。"},
            {"typeMap", "GrassFlowのタイプマップ。通常、これはGrassFlowRendererによって設定されます。何をしているのかわからない限り、触れないでください。"},


            //最適化
            {"_Cull", "レンダリングのカリングモード。メッシュに両面ポリゴンがある場合は「オフ」に設定することをお勧めします。それ以外の場合は、最も効率的であるため、ほとんどの場合、これを背面カリングに設定することをお勧めします。"},
            {"MESH_COLORS", "メッシュのカスタム頂点カラーを使用して、風に対する感度を決定できるようにします。色の赤チャンネルが使用されます。"},
            {"MESH_NORMALS", "メッシュの法線を使用できるようにします。それ以外の場合は、地形の法線が使用されます。単純な草カードの場合、これを有効にすることはおそらく望ましくありません。"},
            {"MESH_UVS", "メッシュのUVを使用できるようにします。テクスチャリングに使用されます。ほとんどの場合、これをオンにすることをお勧めしますが、草にテクスチャを付けない場合はオフにすることもできます。"},
            {"MAP_COLOR", "実行時にカラーマップをペイントする機能を有効にします。それ以外の場合、色はベイクされます。これを有効にすると、シェーダーで追加のテクスチャサンプルが使用されます。モバイルの場合はオフにしておくのが最適です。"},
            {"MAP_PARAM", "実行時にパラメータマップをペイントする機能を有効にします。それ以外の場合、値はベイクされます。これを有効にすると、シェーダーで追加のテクスチャサンプルが使用されます。モバイルの場合はオフにしておくのが最適です。"},
            {"MAP_TYPE",  "実行時にタイプマップをペイントする機能を有効にします。それ以外の場合、値はベイクされます。これを有効にすると、シェーダーで追加のテクスチャサンプルが使用されます。モバイルの場合はオフにしておくのが最適です。"},

            {"GRASS_RIPPLES",  "波紋を受け取る機能を有効にします。これは、波紋を使用していなくてもバッファから読み取る必要があるため、特にモバイルでは高価になる可能性があります。そのため、必要ない場合はオフにしておくのが最適です。"},
            {"GRASS_FORCES",  "草に複数の力を加えることができます。これは、バッファから読み取る必要があるため、特にモバイルでは高価になる可能性があります。オフの場合でも、メインキャラクターに最適な1つの力を草に加えることができます。"},
        };

        static readonly Dictionary<string, string> _messages = new Dictionary<string, string> {
            { "urpDetected", "URPプロジェクトが検出されました。" },
            { "enableURPSupport", "URPサポートを有効にしますか？" },
            { "hdrpDetected", "HDRPプロジェクトが検出されました。" },
            { "enableHDRPSupport", "HDRPサポートを有効にしますか？" },
            { "urpModeActive", "GrassFlowはURPモードです。" },
            { "hdrpModeActive", "GrassFlowはHDRPモードです。" },
            { "enjoyingGrassFlow", "⚠ GrassFlowを楽しんでいますか？ ： " },
            { "leaveReview", "レビューを残す" },
            { "dismiss", "閉じる" },
            { "multipleRenderersWarning", "シーンに複数のGrassFlowRendererがあります！\n1つのレンダラーのみを使用し、すべての地形/メッシュをその単一のインスタンスに割り当てる必要があります。\nそうしないと、パフォーマンスとレンダリングの問題が発生します。" },
            { "terrainTransformMissing", "地形トランスフォームがありません。" },
            { "grassMeshMissing", "草メッシュがありません。" },
            { "terrainMissing", "地形がありません。" },
            { "paintTextureMissing", "選択したペイントタイプのテクスチャがありません。" },
            { "grassMaterialMissing", "草マテリアルがありません。" },
            { "customMeshMissing", "レンダラーコンポーネントにカスタムメッシュが設定されていません。" },
            { "undoRevertMaps", "GrassFlowマップを元に戻す" },
            { "undoChangeVariable", "GrassFlow変数を変更" },
            { "undoAddGrassMesh", "草メッシュを追加" },
            { "undoAddFromSelection", "選択から草メッシュを追加" },
            { "undoDeleteGrassMesh", "草メッシュを削除" },
            { "undoSelectGrassMesh", "GrassFlowメッシュを選択" },
            { "undoSetDetailMap", "GrassFlowディティールマップを設定" },
            { "undoChangeBrush", "GrassFlowブラシを変更" },
            { "missingTerrainLayers", "地形にレイヤーがありません！地形オブジェクトに問題がないか確認してください。" },
            { "noSplatLayers", "地形にスプラットレイヤーがありません。" },
            { "assignTerrainObject", "設定で地形オブジェクトを割り当ててください。" },
            { "undoPaint", "GrassFlowペイント" },
            { "errorPaintTextureMissing", "GrassFlow：選択したペイントモードのテクスチャが設定されていません。" },
            { "errorCantSaveMapNoFile", "テクスチャマップを保存できません！おそらくファイルがないためです。" },
            { "errorMapNotPng", "ディティールマップは.png形式である必要があります！" },
            { "saveDialogTitle", "GrassFlow" },
            { "saveDialogMessage", "GrassFlowディティールマップが変更されました。\n変更を保存しますか？\n\nこれは元に戻せません。" },
            { "saveDialogYes", "はい" },
            { "saveDialogNo", "いいえ" },
            { "undoChangePaintTool", "GrassFlowペイントツールを変更" },

            
			//--------------------------------------------------------------------------------
			//その他---------------------------------------------------------------
			//--------------------------------------------------------------------------------
			{ "languageSelectorTitle", "Grassflow言語セレクター" },
            { "selectLanguagePrompt", "言語を選択してください：" },
            { "languageLabel", "言語：" },
            { "confirmSelectionButton", "選択を確定" },
        };
    }
}