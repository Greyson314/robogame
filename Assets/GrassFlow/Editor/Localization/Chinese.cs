using System.Collections.Generic;

namespace GrassFlow.Localization {
    public class Chinese : Locale {

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

        internal new static string isoCodeStatic => "zh";
        internal override string isoCode => isoCodeStatic;
        internal override Dictionary<string, string> names => _names;
        internal override Dictionary<string, string> tooltips => _tooltips;
        internal override Dictionary<string, string> messages => _messages;
        internal override Dictionary<string, string> materialTooltips => _materialTooltips;


        static readonly Dictionary<string, string> _names = new Dictionary<string, string> {
            { nameof(GrassMesh.asyncInitialization), "异步初始化"},
            { nameof(GrassMesh.receiveShadows), "接收阴影"},
            { nameof(GrassMesh.castShadows), "投射阴影"},
            { nameof(GrassMesh.renderingLayer), "渲染层蒙版"},
            { nameof(GrassMesh.instanceCount), "实例数"},
            { nameof(GrassMesh.lodSteps), "LOD层级"},
            { nameof(GrassMesh.renderType), "渲染类型"},
            { nameof(GrassMesh.grassMesh), "地形网格"},
            { nameof(GrassMesh.customMeshLods), "草LOD"},
            { nameof(GrassMesh.crossFade), "交叉淡入淡出"},
            { nameof(GrassMesh.crossFadeRange), "交叉淡入淡出范围"},
            { nameof(GrassMesh.customGrassMesh), "自定义草网格"},
            { nameof(GrassMesh.mainGrassMat), "主草材质"},
            { nameof(GrassMesh.terrainObject), "地形对象"},
            { nameof(GrassMesh.terrainTransform), "地形变换"},
            { nameof(GrassMesh.colorMap), "颜色贴图"},
            { nameof(GrassMesh.paramMap), "参数贴图"},
            { nameof(GrassMesh.typeMap), "类型贴图"},
            { nameof(GrassMesh.grassPerTri), "每个实例的草"},
            { nameof(GrassMesh.terrainGrassDensity), "地形草密度"},
            { nameof(GrassMesh.normalizeMaxRatio), "归一化最大比率"},
            { nameof(GrassMesh.terrainSlopeThresh), "地形坡度阈值"},
            { nameof(GrassMesh.terrainSlopeFade), "地形坡度淡出"},
            { nameof(GrassMesh.lodParams), "LOD参数"},
            { nameof(GrassMesh.maxRenderDist), "最大渲染距离"},
            { nameof(GrassMesh.expandBounds), "按草高度扩展边界"},
            { nameof(GrassMesh.frustumCull), "视锥体剔除"},
            { nameof(GrassMesh.frustumCullThresh), "视锥体剔除阈值"},
            { nameof(GrassMesh.bakeDensity), "烘焙密度"},
            { nameof(GrassMesh.bakeData), "烘焙数据"},

            { nameof(GrassFlowRenderer.visualizeChunkBounds), "可视化区块边界"},
            { nameof(GrassFlowRenderer.renderLayer), "渲染层"},
            { nameof(GrassFlowRenderer.updateBuffers), "更新缓冲区"},
            { nameof(GrassFlowRenderer.useMaterialInstance), "使用材质实例"},
            { nameof(GrassFlowRenderer.terrainExpansion), "地形扩展"},
            { nameof(GrassFlowRenderer.normalizeMeshDensity), "归一化网格密度"},

            
			//--------------------------------------------------------------------------------
			//检视面板相关---------------------------------------------------------------
			//--------------------------------------------------------------------------------			
            { "refreshButton", "刷新" },
            { "settingsTab", "设置" },
            { "paintModeTab", "绘制模式" },
            { "globalRendererSettings", "全局渲染器设置" },
            { "grassRenderProperties", "草渲染属性" },
            { "terrainHeader", "地形" },
            { "maxRatio", "最大比率" },
            { "lodHeader", "LOD" },
            { "meshLodChunks", "网格LOD区块" },
            { "terrainLodChunks", "地形LOD区块" },
            { "grassMeshesHeader", "草网格" },
            { "addCopyGrassMesh", "添加/复制草网格" },
            { "addFromSelected", "从所选内容添加" },
            { "detailMapsHeader", "细节贴图" },
            { "revertChangesButton", "还原更改" },
            { "bakeDensityToMeshButton", "将密度烘焙到网格" },
            { "settingsHeader", "设置" },
            { "brushColor", "画笔颜色" },
            { "useBrushOpacity", "使用画笔不透明度" },
            { "grassTypeIndex", "草类型索引" },
            { "clampRange", "钳制范围" },
            { "brushSize", "画笔大小" },
            { "brushStrength", "画笔强度" },
            { "raycastLayerMask", "光线投射层蒙版" },
            { "paintContinuously", "连续绘制" },
            { "useDeltaTimePaint", "使用增量时间绘制" },
            { "splatMapsHeader", "Splat贴图" },
            { "splatLayer", "Splat层" },
            { "tolerance", "容差" },
            { "applyAdditive", "应用添加" },
            { "applySubtractive", "应用减去" },
            { "applyReplace", "应用替换" },
            { "paintGrassColor", "绘制草颜色" },
            { "paintGrassDensity", "绘制草密度" },
            { "paintGrassHeight", "绘制草高度" },
            { "paintGrassFlatness", "绘制草平坦度" },
            { "paintGrassWindStrength", "绘制草风强度" },
            { "paintGrassType", "绘制草类型" },
            { "brushesHeader", "画笔" },
            { "noBrushesDefined", "未定义画笔。" },

            
			//--------------------------------------------------------------------------------
			//材质相关---------------------------------------------------------------
			//--------------------------------------------------------------------------------
			// 草属性
            { "_Color", "草地颜色" },
            { "bladeHeight", "叶片高度" },
            { "bladeWidth", "叶片宽度" },
            { "flatTint", "平坦度色调" },
            { "altCol", "变化颜色" },
            { "DISABLE_DECALS", "禁用贴花" },
            { "_ReceiveShadows", "接收阴影" },
            { "specTint", "高光色调" },
            { "_Metallic", "金属感" },
            { "_Gloss", "光泽度" },
            { "_noiseScale2", "次要噪声缩放" },
            { "_noiseSpeed2", "次要噪声速度" },
            { "occMult", "遮挡强度" },
            { "bladeOffset", "叶片偏移" },
            { "bladeSharp", "叶片锐度" },
            { "seekSun", "向日" },
            { "topViewPush", "顶视图调整" },
            { "flatnessMult", "平坦度调整" },
            { "_BILLBOARD", "广告牌" },
            { "variance", "方差(p,h,c,w)" },

            // 光照
            { "_ppLights", "逐像素光照" },
            { "_AO", "AO" },
            { "ambientCO", "环境光" },
            { "ambientCOShadow", "阴影环境光" },
            { "edgeLight", "边缘光" },
            { "edgeLightSharp", "边缘光锐度" },
            { "blendNormal", "混合表面法线" },
            { "_GF_SPECULAR", "启用高光" },
            { "specSmooth", "平滑度" },
            { "specularMult", "高光倍增" },
            { "specHeight", "高光高度调整" },
            { "_GF_NORMAL_MAP", "启用法线贴图" },
            { "normalStrength", "强度" },
            { "bumpMap", "法线贴图" },

            // 自阴影
            { "GF_SELF_SHADOW", "伪自阴影" },
            { "selfShadowWind", "自阴影风" },
            { "selfShadowScaleOffset", "自阴影缩放/偏移" },

            // LOD
            { "_ALPHA_TO_MASK", "Alpha To Mask" },
            { "widthLODscale", "宽度LOD缩放" },
            { "_GF_USE_DITHER", "使用抖动" },
            { "grassFade", "草地淡出" },
            { "grassFadeSharpness", "淡出锐度" },
            { "_LOD_SCALING", "使用LOD缩放" },

            // 风
            { "windMult", "风强度倍增" },
            { "windTint", "风色调" },
            { "_noiseScale", "噪声缩放" },
            { "_noiseSpeed", "噪声速度" },
            { "windDir", "风向" },
            { "windDir2", "次要风向" },

            // 弯曲
            { "_MULTI_SEGMENT", "多段" },
            { "bladeLateralCurve", "曲率" },
            { "bladeVerticalCurve", "下垂" },
            { "bladeStiffness", "柔软度" },

            // 贴图和纹理
            { "_SEMI_TRANSPARENT", "启用Alpha裁剪" },
            { "alphaLock", "丢弃纹理Alpha" },
            { "alphaMult", "Alpha倍增" },
            { "alphaClip", "Alpha裁剪" },
            { "numTextures", "纹理数量" },
            { "textureAtlasScalingCutoff", "类型纹理缩放截止" },
            { "_SpecMap", "高光贴图" },
            { "_OccMap", "遮挡贴图" },
            { "_MainTex", "草地纹理" },
            { "dhfParamMap", "参数贴图" },

            // 优化
            { "_Cull", "剔除模式" },
            { "MESH_COLORS", "使用顶点高度颜色" },
            { "MESH_NORMALS", "使用网格法线" },
            { "MESH_UVS", "使用网格UV" },
            { "MAP_COLOR", "动态颜色贴图" },
            { "MAP_PARAM", "动态参数贴图" },
            { "MAP_TYPE", "动态类型贴图" },
            { "GRASS_RIPPLES", "允许涟漪" },
            { "GRASS_FORCES", "允许多重力" },
        };

        static readonly Dictionary<string, string> _tooltips = new Dictionary<string, string> {
            { nameof(GrassMesh.asyncInitialization), "启用部分多线程异步执行初始处理，可以在拥有大型网格时略微减少加载时间。这样做的缺点是游戏可能会在草地加载完成前开始。"},
            { nameof(GrassMesh.receiveShadows), "在草地上接收阴影。可能会消耗较大资源，尤其是在级联阴影开启时。（需要带有深度通道的草地着色器才能正确渲染）"},
            { nameof(GrassMesh.castShadows), "草地投射阴影。一个相当消耗资源的选项。（同样需要带有深度通道的草地着色器才能渲染）"},
            { nameof(GrassMesh.renderingLayer), "URP/HDRP渲染层蒙版。"},
            { nameof(GrassMesh.instanceCount), "此值与“每个实例的草”和基础源几何体中的三角形数量相乘，以计算可以渲染的总可能实例数。"},
            { nameof(GrassMesh.lodSteps), "LOD系统可以减少草地密度的步数，以减少远处实例的数量。\n重要提示：请确保此值足够高，因为此设置还控制了可以显示的最小草量。即，此设置越高，远处可以显示的草量就越少。"},
            { nameof(GrassMesh.renderType), "此草地的模式。网格会将草地附加到网格的三角形上，地形会将草地附加到unity地形对象的表面。"},
            { nameof(GrassMesh.grassMesh), "在网格模式下附加草地的网格。"},
            { nameof(GrassMesh.customMeshLods), "渲染草地时使用的网格、材质和距离。仅在不使用视锥体剔除时才允许单个LOD。"},
            { nameof(GrassMesh.crossFade), "如果启用，LOD将在一些重叠区域绘制，并在两者之间淡入淡出以平滑过渡。\n在重叠淡入淡出范围内，区块将被绘制两次，并且当前淡入淡出的区块无法进行批处理，因此会对性能产生一些影响。\n注意：必须在用于LOD的材质上启用抖动。"},
            { nameof(GrassMesh.crossFadeRange), "LOD之间交叉淡入淡出的范围"},
            { nameof(GrassMesh.customGrassMesh), "用于渲染草地的网格。"},
            { nameof(GrassMesh.mainGrassMat), "用于渲染草地的材质。应使用GrassFlow着色器之一。"},
            { nameof(GrassMesh.terrainObject), "在地形模式下附加草地的地形对象。"},
            { nameof(GrassMesh.terrainTransform), "草地所属的变换。"},
            { nameof(GrassMesh.colorMap), "控制草地颜色的纹理。此纹理的Alpha通道用于控制颜色的应用方式。如果Alpha为1，则颜色也乘以材质颜色；如果为0，则忽略材质颜色。中间值也可以。"},
            { nameof(GrassMesh.paramMap), "控制草地各种参数的纹理。红色通道 = 密度。绿色通道 = 高度，蓝色通道 = 平坦度。Alpha通道 = 风强度。"},
            { nameof(GrassMesh.typeMap), "控制从草地纹理图集（如果使用）中使用哪个纹理的纹理。注意：请阅读文档以了解此纹理的工作原理。"},
            { nameof(GrassMesh.grassPerTri), "如果不确定，请保留为1。\n复制渲染网格几何体的次数。基本上使更多几何体成为“真实”的，而不是实例化的。\n真实几何体与实例化几何体之间存在一定的阈值，可以达到最快的速度，因此您需要尝试一下，看看什么对您最有效。\n注意：在移动设备上可能有用，也可能没用。\n注意：设置得太高可能会导致奇怪的LOD弹出，对于视锥体剔除应设置为1。"},
            { nameof(GrassMesh.terrainGrassDensity), "在地形模式下渲染的草地基本级别。此数量将乘以实例数以控制LOD衰减。"},
            { nameof(GrassMesh.normalizeMaxRatio), "可以细分最大三角形的最大比率。基本上，它只是控制在尝试归一化网格时的细分密度。您可能希望在仍能提供良好结果的情况下将其设置得尽可能低。"},
            { nameof(GrassMesh.terrainSlopeThresh), "-1到1的角度阈值，用于在地形上生成草地，与向上方向（0, 1, 0）进行比较。"},
            { nameof(GrassMesh.terrainSlopeFade), "草地将缩放以“淡出”的地形坡度阈值距离。"},
            { nameof(GrassMesh.lodParams), "控制草地的LOD参数。X = 渲染距离。Y = 密度衰减锐度（草地数量减少到零的速度）。Z = 偏移量，基本上是一个正数，可防止叶片在此距离内弹出。"},
            { nameof(GrassMesh.maxRenderDist), "控制草地区块的最大渲染距离。此值主要用于快速拒绝远处区块进行渲染。"},
            { nameof(GrassMesh.expandBounds), "如果您正在将草地渲染到不只是朝上的网格（例如球体）上，这很有用，因为草地最终可能会指向任何方向，因此需要按草地的最大潜在高度扩展区块的边界。\n但是由于大多数地形只会向上生长草地，因此在各个方向上不扩展边界可能会更优化。\n请注意，即使禁用此功能，边界仍会垂直扩展。"},
            { nameof(GrassMesh.frustumCull), "是否对草地使用视锥体剔除（丢弃相机视图外的草地）。使用额外的VRAM。通常，除非渲染大量区块，否则这对性能没有太大帮助，并且使用此功能可能会导致阴影出现问题，因为相机视图外的草地很容易在相机视图内投射阴影。\n如果关闭此功能，Unity将简单地按区块处理剔除并单独渲染每个线程。\n如果开启此功能，则计算着色器会手动剔除每个草地实例，然后发出一个绘制调用以一次性渲染所有内容。"},
            { nameof(GrassMesh.frustumCullThresh), "水平和垂直视图的阈值，用于确定草地必须在相机视图外多远才能被剔除。通常，这些值应设置得尽可能低，以免在视图边缘看到草地弹出。"},
            { nameof(GrassMesh.bakeDensity), "如果使用参数贴图，则仅根据密度通道生成草地。这效率要高得多，唯一的缺点是无法在运行时动态绘制草地密度。"},
            { nameof(GrassMesh.bakeData), "增加内存成本（37%），但在使用颜色/参数/类型贴图时效率更高。\n如果禁用此功能并使用贴图，则应在材质上为这些贴图启用动态贴图设置。\n重要提示：视锥体剔除目前与关闭此设置不兼容。"},

            { nameof(GrassFlowRenderer.visualizeChunkBounds), "这真的需要工具提示吗？嗯，好吧，区块边界会按叶片高度自动扩展，以避免在以奇怪的角度剔除边界时草地弹出。"},
            { nameof(GrassFlowRenderer.renderLayer), "在其上渲染草地的图层。"},
            { nameof(GrassFlowRenderer.updateBuffers), "此设置仅影响编辑器。大多数情况下，您会希望启用此设置，因为它可以防止在重新编译脚本等时出现视觉弹出。" +
            "您可以关闭它以更准确地了解游戏性能，尽管实际上几乎没有任何区别。"},
            { nameof(GrassFlowRenderer.useMaterialInstance), "如果为true，则将创建材质的实例以进行渲染。如果您想为多个草地使用相同的材质但又希望它们具有不同的纹理等，这一点很重要。"},
            { nameof(GrassFlowRenderer.terrainExpansion), "在地形上扩展草地区块的数量，有助于避免区块边缘的瑕疵。最好将其设置得尽可能低，而不会看起来很糟糕。"},
            { nameof(GrassFlowRenderer.normalizeMeshDensity), "除非您的源网格具有非常不均匀的密度，否则不要启用此设置，因为它会增加处理时间并可能产生更差的结果。" +
            "此设置会尝试细分网格，使所有三角形的大小尽可能接近，原始形状将完全匹配。" +
            "由于这会细分网格，因此您可能需要减少GrassPerTri以考虑增加的密度。"},

            
			//--------------------------------------------------------------------------------
			//检视面板相关---------------------------------------------------------------
			//--------------------------------------------------------------------------------
            { "refreshButton", "释放/销毁所有当前数据并重置所有内容。用于在更改某些内容后重置草地。" },
            { "globalRendererSettings", "这些设置在所有草地网格之间共享" },
            { "meshLodChunks", "用于LOD剔除的区块数量。到每个区块的距离控制将在那里渲染的草地数量。在网格模式下，通常您在Y方向上不需要超过一个区块，但如果您有非常垂直的地形，它可能会很有用。区块太多对性能不利，但区块太少在剔除草地时会看起来很糟糕和块状，因此请将其设置为尽可能少的区块，同时又不会看起来很糟糕。（提示：您不需要像您想象的那么多。）" },
            { "terrainLodChunks", "用于LOD剔除的区块数量。到每个区块的距离控制将在那里渲染的草地数量。区块太多对性能不利，但区块太少在剔除草地时会看起来很糟糕和块状，因此请将其设置为尽可能少的区块，同时又不会看起来很糟糕。（提示：您不需要像您想象的那么多。）" },
            { "addCopyGrassMesh", "添加一个额外的GrassMesh，可以在单独的网格或地形上渲染草地，并从当前选择的GrassMesh复制设置。" },
            { "addFromSelected", "尝试从层次结构中选定的对象添加额外的GrassMesh，自动填充变换和网格。您可能需要锁定检视面板，然后选择对象才能使其正常工作。" },
            { "revertChangesButton", "放弃自上次保存以来对细节贴图的更改。每当保存项目资源（例如按Ctrl+S）时，都会保存贴图。还原热键：Shift-R。此操作“应该”具有撤消/重做支持，它可能有效。" },
            { "bakeDensityToMeshButton", "根据参数贴图中的密度信息创建一个新网格。您可以使用此网格更有效地仅在网格的某些部分渲染草地。不会自动应用生成的网格。" },
            { "useBrushOpacity", "绘制时是否使用画笔不透明度。在以全强度绘制草地类型时，关闭此功能是理想的选择，以避免画笔不透明度对密度产生不良影响的瑕疵。" },
            { "grassTypeIndex", "草地纹理图集中的索引。用于选择要绘制的纹理。" },
            { "clampRange", "绘制时参数的最小和最大范围。这可以用于基本上绘制一个设定值，而不是加法或减法。" },
            { "raycastLayerMask", "此蒙版用于在绘制时对地形/网格进行光线投射。您可以使用此蒙版仅在地形所在的图层上绘制，并穿过阻挡对象进行绘制，反之亦然。" },
            { "paintContinuously", "如果关闭，则需要移动鼠标才能绘制，否则在按住鼠标时会连续绘制。" },
            { "useDeltaTimePaint", "如果开启，画笔强度将乘以增量时间，使绘制强度与帧率无关。如果您想更像图章一样使用画笔，并使用强度为1并单击一次将整个画笔应用于草地，则关闭此功能很有用。" },
            { "splatLayer", "您想要用来遮盖草地出现位置的splat纹理层的索引。" },
            { "tolerance", "应用splat贴图层时控制不透明度容差。" },
            { "applyAdditive", "根据选定的图层添加草地，但不移除任何现有的草地。" },
            { "applySubtractive", "根据选定的图层移除草地，但不会影响splat贴图之外的草地。" },
            { "applyReplace", "根据选定的图层添加草地，移除并覆盖现有的草地。" },
            { "paintGrassColor", "单击以绘制颜色。简单。" },
            { "paintGrassDensity", "单击以填充草地。按住Shift键单击以擦除草地。" },
            { "paintGrassHeight", "单击以升高草地。按住Shift键单击以降低草地。" },
            { "paintGrassFlatness", "单击以压平草地。按住Shift键单击以取消压平。" },
            { "paintGrassWindStrength", "单击以增加风强度。按住Shift键单击以减小。" },
            { "paintGrassType", "单击以绘制草地纹理图集（如果使用）中显示的纹理。按住Shift键单击以绘制第一个纹理。画笔强度控制所选类型的密度。" },
            { "paintToolColor", "颜色" },
            { "paintToolDensity", "密度" },
            { "paintToolHeight", "高度" },
            { "paintToolFlatness", "平坦度" },
            { "paintToolWind", "风强度" },
            { "paintToolType", "草地类型" },
        };

        static Dictionary<string, string> _materialTooltips = new Dictionary<string, string>() {
            { "_Color", "草叶的基本颜色。" },
            { "bladeHeight", "草叶的整体高度。" },
            { "bladeWidth", "草叶的整体宽度。" },
            { "flatTint", "应用于压平草地的颜色色调。" },
            { "altCol", "用于变化的替代颜色。" },
            { "DISABLE_DECALS", "禁用草地上的贴花。" },
            { "_ReceiveShadows", "草地是否应该接收阴影。" },
            { "specTint", "高光反射的颜色色调。" },
            { "_Metallic", "控制草地的金属外观。" },
            { "_Gloss", "控制草地用于高光反射的光泽度。" },
            { "_noiseScale2", "次要风噪声的规模。" },
            { "_noiseSpeed2", "次要风噪声的速度。" },
            { "occMult", "遮挡贴图的强度。" },

            {"bladeOffset", "向地形上的草地位置添加高度偏移，可用于微调。"},
            {"bladeSharp", "控制草叶的锐度，0为完美尖点，1为矩形。"},
            {"seekSun", "控制草地与表面法线的对齐程度。0表示完全对齐，1表示指向上方。"},
            {"topViewPush", "尝试在从上方查看时为草地添加轻微偏移，这有助于在向下看时提供更多深度和密度。"},
            {"flatnessMult", "控制在使用参数贴图的平坦度通道时，草地被推平的程度。"},
            {"_BILLBOARD", "草地是否应始终面向相机。"},
            {"variance", "这四个值控制草地在某些方面的随机化程度。这些值是：X = 位置，Y = 高度，Z = 颜色，W = 宽度"},

            //光照
            {"_ppLights","按像素计算着色。稍慢，仅在使用自定义草地网格时才真正明显，但对于法线贴图是必需的。"},
            {"_AO", "控制草叶底部的黑暗程度，0表示更暗，1表示没有黑暗。"},
            {"ambientCO", "控制着色的黑暗程度。"},
            {"ambientCOShadow", "在光源阴影强度设置的基础上，这允许您进一步调整接收到的阴影强度。"},
            {"edgeLight", "控制当光线方向与草叶边缘成直角时增加的亮度强度。"},
            {"edgeLightSharp", "控制增加的边缘光亮度的锐度。"},
            {"blendNormal", "将网格法线与地形表面法线混合。这可以更好地控制着色和高光。"},
            {"_GF_SPECULAR", "启用高光。在最坏的情况下会增加约0.1毫秒的少量性能成本。"},
            {"specSmooth", "控制用于高光/反射的表面的平滑度/模糊度。"},
            {"specularMult", "高光强度的倍增器。"},
            {"specHeight", "高光反射的高度调整，可用于调整，使草地底部没有高光。"},
            {"_GF_NORMAL_MAP", "启用法线贴图。在最坏的情况下会产生中等性能成本，约为1毫秒，在合理的情况下为0.1毫秒。"},
            {"normalStrength", "法线贴图效果的强度。"},
            {"bumpMap", "用于法线贴图的纹理。"},

            //自阴影
            {"GF_SELF_SHADOW", "启用一种廉价技术，可以在不实际渲染阴影的情况下为草地添加假阴影。这基本上是通过从主光的角度将草地纹理重新投影到草地上来实现的。\n" +
                "假设您的草地网格的顶点在x/z轴上为-0.5到0.5，在y轴上为0到1。\n" +
                "使用抠图纹理和草地卡片效果最佳。"},
            {"selfShadowWind", "自阴影受风的影响程度，以使其运动。"},
            {"selfShadowScaleOffset", "（x，y）：应用于阴影投影的比例。\n（z，w）：应用于阴影投影的偏移量。\n您可以调整这些参数，为您的特定网格微调位置。"},


            //LOD
            {"_ALPHA_TO_MASK", "如果启用，将在着色器中打开AlphaToMask。而它的性能相当复杂。有时启用它草地看起来更好，有时则不然。"},
            {"widthLODscale", "控制随着与相机距离的增加，叶片宽度的增长方式。这有助于用更少的草地覆盖相同的区域，而不会很明显。"},
            {"_GF_USE_DITHER", "将在一定距离内对草地进行抖动，以进一步隐藏LOD过渡，或者在延迟模式下始终进行抖动。" +
                "大多数情况下，开启此功能效果更好，但会产生一些可能不希望出现的瑕疵。" +
                "除非您注意到LOD过渡处出现特别严重的弹出，否则请关闭此功能"},
            {"grassFade", "草地视觉上淡出的距离。注意：这不控制LOD设置，那些必须从GrassFlow组件中单独设置，此设置仅为视觉效果。"},
            {"grassFadeSharpness", "草地淡出的锐度。"},
            {"_LOD_SCALING", "将垂直缩放草地以实现LOD淡入。"},

            //风
            {"windMult", "整体风强度倍增器。"},
            {"windTint", "当风强烈影响草地时，草地被染上的颜色，Alpha控制强度。"},
            {"_noiseScale", "风的噪声采样的比例，有点像控制风的阵风大小。"},
            {"_noiseSpeed", "噪声在草地上滚动的速度，以改变风的模式。有点像风速，但您需要调整风的强度以匹配。"},
            {"windDir  ", "风吹的方向，这些值的大小基本上决定了强度。"},
            {"windDir2", "与风向相同，但控制次要风向，有助于使风更多样化，而不是总是朝一个方向吹。"},
            
            //弯曲
            {"_MULTI_SEGMENT", "为每个草叶添加额外的分段，使其可以因风或曲率而弯曲。" +
                "可以通过调整GrassFlow/Shaders/GrassStructsVars.cginc文件顶部的数字来更改分段的最小和最大数量。" +
                "根据LOD设置，草分段的数量会随着距离的增加而减少。"},
            {"bladeLateralCurve", "草地具有的自然弯曲程度。"},
            {"bladeVerticalCurve", "有点像将草地向下拉向表面。"},
            {"bladeStiffness", "控制草地响应风/涟漪的弯曲程度。"},

            //贴图和纹理
            {"_SEMI_TRANSPARENT", "启用使用带Alpha的纹理。"},
            {"alphaLock", "在应用Alpha裁剪的同时丢弃草地纹理本身的Alpha。如果您的纹理Alpha效果不佳或您只是不想使用它，这可能很有用。"},
            {"alphaMult", "纹理Alpha的倍增器，增加此值可以微调纹理的Alpha，如果它不够清晰。"},
            {"alphaClip", "控制透明纹理裁剪的灵敏度。"},
            {"numTextures", "将其设置为类型贴图纹理图集中的纹理数量。仅在使用类型贴图时使用。"},
            {"textureAtlasScalingCutoff", "LOD宽度缩放关闭的类型贴图的纹理索引。例如：将其设置为3，缩放将仅应用于图集中的前三个纹理。" +
                "仅在使用类型贴图时使用。"},
            {"_SpecMap", "用于延迟渲染的高光贴图。"},
            {"_OccMap", "用于延迟渲染的遮挡贴图。"},
            {"_MainTex", "用于细化草叶/四边形的纹理。这是用于Alpha裁剪的纹理。可以是与类型贴图结合使用的水平纹理图集，请确保同时设置纹理数量属性。"},
            {"colorMap", "GrassFlow的颜色贴图。通常由GrassFlowRenderer设置，除非您知道自己在做什么，否则不要触摸它。"},
            {"dhfParamMap", "GrassFlow的参数贴图。通常由GrassFlowRenderer设置，除非您知道自己在做什么，否则不要触摸它。"},
            {"typeMap", "GrassFlow的类型贴图。通常由GrassFlowRenderer设置，除非您知道自己在做什么，否则不要触摸它。"},


            //优化
            {"_Cull", "渲染的剔除模式。如果您的网格具有双面多边形，您可能希望将其设置为“关闭”。否则，大多数情况下，您可能只想将其设置为背面剔除，因为这样最有效率。"},
            {"MESH_COLORS", "启用在网格上使用自定义顶点颜色来确定对风的敏感度。使用颜色的红色通道。"},
            {"MESH_NORMALS", "启用使用网格上的法线，否则使用地形的法线。对于简单的草地卡片，您可能不希望启用此功能。"},
            {"MESH_UVS", "启用使用网格上的UV，用于纹理。您几乎总是希望启用此功能，但如果您不为草地添加纹理，也可以将其关闭。"},
            {"MAP_COLOR", "启用在运行时绘制颜色贴图的功能。否则，颜色是烘焙的。启用此功能会在着色器中使用一个额外的纹理采样。最好在移动设备上关闭此功能。"},
            {"MAP_PARAM", "启用在运行时绘制参数贴图的功能。否则，值是烘焙的。启用此功能会在着色器中使用一个额外的纹理采样。最好在移动设备上关闭此功能。"},
            {"MAP_TYPE",  "启用在运行时绘制类型贴图的功能。否则，值是烘焙的。启用此功能会在着色器中使用一个额外的纹理采样。最好在移动设备上关闭此功能。"},

            {"GRASS_RIPPLES",  "启用接收涟漪的功能。这可能很耗费资源，尤其是在移动设备上，因为它需要从缓冲区读取，即使您不使用涟漪，所以如果不需要，最好关闭它。"},
            {"GRASS_FORCES",  "允许在草地上施加多个力，这可能很耗费资源，尤其是在移动设备上，因为它需要从缓冲区读取。如果关闭，仍然可以向草地施加一个力，最好用于主角。"},
        };

        static readonly Dictionary<string, string> _messages = new Dictionary<string, string> {
            { "urpDetected", "检测到URP项目。" },
            { "enableURPSupport", "启用URP支持？" },
            { "hdrpDetected", "检测到HDRP项目。" },
            { "enableHDRPSupport", "启用HDRP支持？" },
            { "urpModeActive", "GrassFlow处于URP模式。" },
            { "hdrpModeActive", "GrassFlow处于HDRP模式。" },
            { "enjoyingGrassFlow", "⚠ 喜欢GrassFlow吗？：" },
            { "leaveReview", "留下评论" },
            { "dismiss", "忽略" },
            { "multipleRenderersWarning", "场景中有多个GrassFlowRenderer！\n您应该只使用一个渲染器，并将所有地形/网格分配给该单个实例。\n否则会导致性能和渲染问题。" },
            { "terrainTransformMissing", "缺少地形变换。" },
            { "grassMeshMissing", "缺少草地网格。" },
            { "terrainMissing", "缺少地形。" },
            { "paintTextureMissing", "缺少所选绘制类型的纹理。" },
            { "grassMaterialMissing", "缺少草地材质。" },
            { "customMeshMissing", "渲染器组件中未设置自定义网格。" },
            { "undoRevertMaps", "GrassFlow还原贴图" },
            { "undoChangeVariable", "GrassFlow更改变量" },
            { "undoAddGrassMesh", "添加草地网格" },
            { "undoAddFromSelection", "从所选内容添加草地网格" },
            { "undoDeleteGrassMesh", "删除草地网格" },
            { "undoSelectGrassMesh", "选择GrassFlow网格" },
            { "undoSetDetailMap", "GrassFlow设置细节贴图" },
            { "undoChangeBrush", "GrassFlow更改画笔" },
            { "missingTerrainLayers", "地形缺少图层！请检查您的地形对象是否有问题。" },
            { "noSplatLayers", "地形上没有splat图层。" },
            { "assignTerrainObject", "请在设置中分配地形对象。" },
            { "undoPaint", "GrassFlow绘制" },
            { "errorPaintTextureMissing", "GrassFlow：未设置所选绘制模式的纹理。" },
            { "errorCantSaveMapNoFile", "无法保存纹理贴图！可能是因为它没有文件。" },
            { "errorMapNotPng", "细节贴图需要是.png格式！" },
            { "saveDialogTitle", "GrassFlow" },
            { "saveDialogMessage", "GrassFlow细节贴图已修改。\n保存更改？\n\n此操作无法撤消。" },
            { "saveDialogYes", "是" },
            { "saveDialogNo", "否" },
            { "undoChangePaintTool", "GrassFlow更改绘制工具" },

            
			//--------------------------------------------------------------------------------
			//杂项---------------------------------------------------------------
			//--------------------------------------------------------------------------------
			{ "languageSelectorTitle", "Grassflow语言选择器" },
            { "selectLanguagePrompt", "请选择一种语言：" },
            { "languageLabel", "语言：" },
            { "confirmSelectionButton", "确认选择" },
        };
    }
}