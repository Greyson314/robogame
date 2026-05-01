using System.Collections.Generic;

namespace GrassFlow.Localization {
    public class Korean : Locale {

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

        internal new static string isoCodeStatic => "ko";
        internal override string isoCode => isoCodeStatic;
        internal override Dictionary<string, string> names => _names;
        internal override Dictionary<string, string> tooltips => _tooltips;
        internal override Dictionary<string, string> messages => _messages;
        internal override Dictionary<string, string> materialTooltips => _materialTooltips;


        static readonly Dictionary<string, string> _names = new Dictionary<string, string> {
            { nameof(GrassMesh.asyncInitialization), "비동기 초기화"},
            { nameof(GrassMesh.receiveShadows), "그림자 받기"},
            { nameof(GrassMesh.castShadows), "그림자 드리우기"},
            { nameof(GrassMesh.renderingLayer), "렌더링 레이어 마스크"},
            { nameof(GrassMesh.instanceCount), "인스턴스 수"},
            { nameof(GrassMesh.lodSteps), "LOD 단계"},
            { nameof(GrassMesh.renderType), "렌더 타입"},
            { nameof(GrassMesh.grassMesh), "지형 메시"},
            { nameof(GrassMesh.customMeshLods), "잔디 LOD"},
            { nameof(GrassMesh.crossFade), "크로스페이드"},
            { nameof(GrassMesh.crossFadeRange), "크로스페이드 범위"},
            { nameof(GrassMesh.customGrassMesh), "사용자 정의 잔디 메시"},
            { nameof(GrassMesh.mainGrassMat), "메인 잔디 재질"},
            { nameof(GrassMesh.terrainObject), "지형 오브젝트"},
            { nameof(GrassMesh.terrainTransform), "지형 트랜스폼"},
            { nameof(GrassMesh.colorMap), "컬러 맵"},
            { nameof(GrassMesh.paramMap), "파라미터 맵"},
            { nameof(GrassMesh.typeMap), "타입 맵"},
            { nameof(GrassMesh.grassPerTri), "인스턴스당 잔디"},
            { nameof(GrassMesh.terrainGrassDensity), "지형 잔디 밀도"},
            { nameof(GrassMesh.normalizeMaxRatio), "정규화 최대 비율"},
            { nameof(GrassMesh.terrainSlopeThresh), "지형 경사 임계값"},
            { nameof(GrassMesh.terrainSlopeFade), "지형 경사 페이드"},
            { nameof(GrassMesh.lodParams), "LOD 파라미터"},
            { nameof(GrassMesh.maxRenderDist), "최대 렌더 거리"},
            { nameof(GrassMesh.expandBounds), "잔디 높이로 경계 확장"},
            { nameof(GrassMesh.frustumCull), "프러스텀 컬링"},
            { nameof(GrassMesh.frustumCullThresh), "프러스텀 컬링 임계값"},
            { nameof(GrassMesh.bakeDensity), "밀도 베이크"},
            { nameof(GrassMesh.bakeData), "데이터 베이크"},

            { nameof(GrassFlowRenderer.visualizeChunkBounds), "청크 경계 시각화"},
            { nameof(GrassFlowRenderer.renderLayer), "렌더 레이어"},
            { nameof(GrassFlowRenderer.updateBuffers), "버퍼 업데이트"},
            { nameof(GrassFlowRenderer.useMaterialInstance), "머티리얼 인스턴스 사용"},
            { nameof(GrassFlowRenderer.terrainExpansion), "지형 확장"},
            { nameof(GrassFlowRenderer.normalizeMeshDensity), "메시 밀도 정규화"},

            
			//--------------------------------------------------------------------------------
			//인스펙터 관련---------------------------------------------------------------
			//--------------------------------------------------------------------------------			
            { "refreshButton", "새로 고침" },
            { "settingsTab", "설정" },
            { "paintModeTab", "페인트 모드" },
            { "globalRendererSettings", "전역 렌더러 설정" },
            { "grassRenderProperties", "잔디 렌더 속성" },
            { "terrainHeader", "지형" },
            { "maxRatio", "최대 비율" },
            { "lodHeader", "LOD" },
            { "meshLodChunks", "메시 LOD 청크" },
            { "terrainLodChunks", "지형 LOD 청크" },
            { "grassMeshesHeader", "잔디 메시" },
            { "addCopyGrassMesh", "잔디 메시 추가/복사" },
            { "addFromSelected", "선택 항목에서 추가" },
            { "detailMapsHeader", "디테일 맵" },
            { "revertChangesButton", "변경 사항 되돌리기" },
            { "bakeDensityToMeshButton", "밀도를 메시로 베이크" },
            { "settingsHeader", "설정" },
            { "brushColor", "브러시 색상" },
            { "useBrushOpacity", "브러시 불투명도 사용" },
            { "grassTypeIndex", "잔디 타입 인덱스" },
            { "clampRange", "클램프 범위" },
            { "brushSize", "브러시 크기" },
            { "brushStrength", "브러시 강도" },
            { "raycastLayerMask", "레이캐스트 레이어 마스크" },
            { "paintContinuously", "연속 페인팅" },
            { "useDeltaTimePaint", "델타 타임 페인팅 사용" },
            { "splatMapsHeader", "스플랫 맵" },
            { "splatLayer", "스플랫 레이어" },
            { "tolerance", "허용 오차" },
            { "applyAdditive", "가산 적용" },
            { "applySubtractive", "감산 적용" },
            { "applyReplace", "대체 적용" },
            { "paintGrassColor", "잔디 색상 페인트" },
            { "paintGrassDensity", "잔디 밀도 페인트" },
            { "paintGrassHeight", "잔디 높이 페인트" },
            { "paintGrassFlatness", "잔디 평탄도 페인트" },
            { "paintGrassWindStrength", "잔디 바람 강도 페인트" },
            { "paintGrassType", "잔디 타입 페인트" },
            { "brushesHeader", "브러시" },
            { "noBrushesDefined", "정의된 브러시가 없습니다." },

            
			//--------------------------------------------------------------------------------
			//머티리얼 관련---------------------------------------------------------------
			//--------------------------------------------------------------------------------
			// 잔디 속성
            { "_Color", "잔디 색" },
            { "bladeHeight", "잎사귀 높이" },
            { "bladeWidth", "잎사귀 너비" },
            { "flatTint", "평탄도 틴트" },
            { "altCol", "변형 색상" },
            { "DISABLE_DECALS", "데칼 비활성화" },
            { "_ReceiveShadows", "그림자 받기" },
            { "specTint", "반사광 틴트" },
            { "_Metallic", "금속성" },
            { "_Gloss", "광택" },
            { "_noiseScale2", "보조 노이즈 스케일" },
            { "_noiseSpeed2", "보조 노이즈 속도" },
            { "occMult", "차폐 강도" },
            { "bladeOffset", "블레이드 오프셋" },
            { "bladeSharp", "블레이드 날카로움" },
            { "seekSun", "태양 찾기" },
            { "topViewPush", "상단 뷰 조정" },
            { "flatnessMult", "평탄도 조정" },
            { "_BILLBOARD", "빌보드" },
            { "variance", "분산 (p,h,c,w)" },

            // 조명
            { "_ppLights", "픽셀당 조명" },
            { "_AO", "AO" },
            { "ambientCO", "앰비언트" },
            { "ambientCOShadow", "그림자 앰비언트" },
            { "edgeLight", "엣지 라이트" },
            { "edgeLightSharp", "엣지 라이트 날카로움" },
            { "blendNormal", "표면 노멀 혼합" },
            { "_GF_SPECULAR", "스페큘러 활성화" },
            { "specSmooth", "매끄러움" },
            { "specularMult", "스페큘러 배율" },
            { "specHeight", "스페큘러 높이 조정" },
            { "_GF_NORMAL_MAP", "노멀 매핑 활성화" },
            { "normalStrength", "강도" },
            { "bumpMap", "노멀 맵" },

            // 셀프 섀도우
            { "GF_SELF_SHADOW", "가짜 셀프 섀도우" },
            { "selfShadowWind", "셀프 섀도우 바람" },
            { "selfShadowScaleOffset", "셀프 섀도우 스케일/오프셋" },

            // LOD
            { "_ALPHA_TO_MASK", "알파를 마스크로" },
            { "widthLODscale", "너비 LOD 스케일" },
            { "_GF_USE_DITHER", "디더링 사용" },
            { "grassFade", "잔디 페이드" },
            { "grassFadeSharpness", "페이드 날카로움" },
            { "_LOD_SCALING", "LOD 스케일링 사용" },

            // 바람
            { "windMult", "바람 강도 배율" },
            { "windTint", "바람 색조" },
            { "_noiseScale", "노이즈 스케일" },
            { "_noiseSpeed", "노이즈 속도" },
            { "windDir", "바람 방향" },
            { "windDir2", "보조 바람 방향" },

            // 굽힘
            { "_MULTI_SEGMENT", "다중 세그먼트" },
            { "bladeLateralCurve", "곡률" },
            { "bladeVerticalCurve", "처짐" },
            { "bladeStiffness", "뻣뻣함" },

            // 맵 및 텍스처링
            { "_SEMI_TRANSPARENT", "알파 클립 활성화" },
            { "alphaLock", "텍스처 알파 무시" },
            { "alphaMult", "알파 배율" },
            { "alphaClip", "알파 클립" },
            { "numTextures", "텍스처 수" },
            { "textureAtlasScalingCutoff", "타입 텍스처 스케일링 컷오프" },
            { "_SpecMap", "스페큘러 맵" },
            { "_OccMap", "오클루전 맵" },
            { "_MainTex", "잔디 텍스처" },
            { "dhfParamMap", "파라미터 맵" },

            // 최적화
            { "_Cull", "컬링 모드" },
            { "MESH_COLORS", "정점 높이 색상 사용" },
            { "MESH_NORMALS", "메시 노멀 사용" },
            { "MESH_UVS", "메시 UV 사용" },
            { "MAP_COLOR", "동적 컬러 맵" },
            { "MAP_PARAM", "동적 파라미터 맵" },
            { "MAP_TYPE", "동적 타입 맵" },
            { "GRASS_RIPPLES", "리플 허용" },
            { "GRASS_FORCES", "다중 힘 허용" },
        };

        static readonly Dictionary<string, string> _tooltips = new Dictionary<string, string> {
            { nameof(GrassMesh.asyncInitialization), "초기 처리의 부분적인 다중 스레드 비동기 실행을 활성화하여 큰 메시지가 있는 경우 로드 시간을 약간 줄일 수 있습니다. 단점은 잔디가 로드되기 전에 게임이 시작될 수 있다는 것입니다."},
            { nameof(GrassMesh.receiveShadows), "잔디에 그림자를 받습니다. 특히 계단식 그림자가 켜져 있으면 비용이 많이들 수 있습니다. (올바르게 렌더링하려면 깊이 패스가 있는 잔디 셰이더가 필요합니다)"},
            { nameof(GrassMesh.castShadows), "잔디가 그림자를 드리웁니다. 상당히 비용이 많이 드는 옵션입니다. (전혀 렌더링하려면 깊이 패스가 있는 잔디 셰이더도 필요합니다)"},
            { nameof(GrassMesh.renderingLayer), "URP/HDRP 렌더링 레이어 마스크."},
            { nameof(GrassMesh.instanceCount), "이 값은 '인스턴스당 잔디' 및 기본 소스 지오메트리의 삼각형 수와 함께 고려되어 렌더링할 수 있는 총 가능한 인스턴스를 계산합니다."},
            { nameof(GrassMesh.lodSteps), "LOD 시스템으로 잔디 밀도를 줄여 원거리의 인스턴스 수를 줄일 수 있는 단계 수입니다.\n중요: 이 설정은 표시할 수 있는 최소 잔디 양도 제어하므로 이 값을 충분히 높게 설정해야 합니다. 즉, 이 설정이 높을수록 원거리에 더 적은 양의 잔디를 표시할 수 있습니다."},
            { nameof(GrassMesh.renderType), "이 잔디의 모드입니다. 메시는 잔디를 메시의 삼각형에 부착하고, 지형은 잔디를 유니티 지형 개체의 표면에 부착합니다."},
            { nameof(GrassMesh.grassMesh), "메시 모드에서 잔디를 부착할 메시입니다."},
            { nameof(GrassMesh.customMeshLods), "잔디를 렌더링할 때 사용할 메시, 머티리얼 및 거리입니다. 개별 lod는 프러스텀 컬링을 사용하지 않을 때만 허용됩니다."},
            { nameof(GrassMesh.crossFade), "활성화하면 LOD가 일부 겹쳐서 그려지고 전환을 부드럽게 하기 위해 페이드됩니다.\n겹침 페이드 범위 동안 청크가 두 번 그려지고 현재 페이드 중인 청크를 일괄 처리할 수 없으므로 성능에 약간의 영향이 있습니다.\n참고: LOD에 사용되는 머티리얼에서 디더링을 활성화해야 합니다."},
            { nameof(GrassMesh.crossFadeRange), "LOD 간에 크로스페이드가 발생하는 범위"},
            { nameof(GrassMesh.customGrassMesh), "잔디를 렌더링하는 데 사용되는 메시입니다."},
            { nameof(GrassMesh.mainGrassMat), "잔디를 렌더링하는 데 사용되는 머티리얼입니다. GrassFlow 셰이더 중 하나를 사용해야 합니다."},
            { nameof(GrassMesh.terrainObject), "지형 모드에서 잔디를 부착할 지형 개체입니다."},
            { nameof(GrassMesh.terrainTransform), "잔디가 속한 트랜스폼입니다."},
            { nameof(GrassMesh.colorMap), "잔디 색상을 제어하는 텍스처입니다. 이 텍스처의 알파 채널은 색상이 적용되는 방식을 제어하는 데 사용됩니다. 알파가 1이면 색상도 머티리얼 색상으로 곱해지고, 0이면 머티리얼 색상이 무시됩니다. 중간 값도 작동합니다."},
            { nameof(GrassMesh.paramMap), "잔디의 다양한 매개변수를 제어하는 텍스처입니다. 빨간색 채널 = 밀도. 녹색 채널 = 높이, 파란색 채널 = 평탄도. 알파 채널 = 바람 강도."},
            { nameof(GrassMesh.typeMap), "잔디 텍스처 아틀라스(사용하는 경우)에서 사용할 텍스처를 제어하는 텍스처입니다. 참고: 이 텍스처가 작동하는 방식에 대한 정보는 설명서를 참조하십시오."},
            { nameof(GrassMesh.grassPerTri), "확실하지 않은 경우 1로 두십시오.\n렌더링된 메시 지오메트리를 복제하는 횟수입니다. 기본적으로 더 많은 지오메트리가 인스턴스화되는 대신 '실제'가 되도록 합니다.\n실제 지오메트리와 인스턴스화된 지오메트리 간에는 가장 빠른 특정 임계값이 있으므로 자신에게 적합한 것을 확인하려면 여러 번 시도해봐야 합니다.\n참고: 모바일에서는 도움이 될 수도 있고 안 될 수도 있습니다.\n참고: 너무 높게 설정하면 이상한 lod 팝핑이 발생할 수 있으며 프러스텀 컬링을 위해 1로 설정해야 합니다."},
            { nameof(GrassMesh.terrainGrassDensity), "지형 모드에서 렌더링할 잔디의 기본 수준입니다. 이 양은 인스턴스 수로 곱해져 LOD 폴오프를 제어합니다."},
            { nameof(GrassMesh.normalizeMaxRatio), "가장 큰 삼각형을 세분화할 수 있는 최대 비율입니다. 기본적으로 메시를 정규화하려고 할 때 세분화 밀도를 제어합니다. 좋은 결과를 제공하면서 가능한 한 낮게 설정하는 것이 좋습니다."},
            { nameof(GrassMesh.terrainSlopeThresh), "지형에 잔디를 생성하기 위한 위쪽 방향(0, 1, 0)과 비교한 -1에서 1까지의 각도 임계값입니다."},
            { nameof(GrassMesh.terrainSlopeFade), "잔디가 '페이드 아웃'되도록 스케일링되는 지형 경사 임계값으로부터의 거리입니다."},
            { nameof(GrassMesh.lodParams), "잔디의 LOD 매개변수를 제어합니다. X = 렌더 거리. Y = 밀도 폴오프 날카로움(잔디의 양이 0으로 감소하는 속도). Z = 오프셋, 기본적으로 양수는 이 거리 내에서 블레이드가 튀어나오는 것을 방지합니다."},
            { nameof(GrassMesh.maxRenderDist), "잔디 청크의 최대 렌더 거리를 제어합니다. 이 값은 주로 렌더링에서 멀리 떨어진 청크를 빠르게 거부하는 데 사용됩니다."},
            { nameof(GrassMesh.expandBounds), "구와 같이 위쪽을 향하지 않는 메시에 잔디를 렌더링하는 경우 유용합니다. 잔디가 어떤 방향으로든 향할 수 있으므로 청크의 경계를 잔디의 최대 잠재적 높이만큼 확장해야 합니다.\n그러나 대부분의 지형에는 위쪽을 향한 잔디만 있으므로 모든 방향으로 경계를 확장하지 않는 것이 더 최적일 수 있습니다.\n이 기능이 비활성화된 경우에도 경계는 여전히 수직으로 확장됩니다."},
            { nameof(GrassMesh.frustumCull), "잔디에 대해 프러스텀 컬링(카메라 뷰 외부의 잔디 버리기)을 사용할지 여부입니다. 추가 VRAM을 사용합니다. 일반적으로 많은 청크 수를 렌더링하지 않는 한 성능에 큰 도움이 되지 않으며, 카메라 뷰 외부의 잔디가 카메라 뷰 안으로 그림자를 드리우기 쉽기 때문에 그림자에 문제가 발생할 수 있습니다.\n이 기능이 꺼져 있으면 Unity는 청크별로 컬링을 처리하고 각 스레드를 개별적으로 렌더링합니다.\n이 기능이 켜져 있으면 컴퓨트 셰이더가 각 잔디 인스턴스를 수동으로 컬링한 다음 하나의 드로우 콜이 실행되어 모든 것을 한 번에 렌더링합니다."},
            { nameof(GrassMesh.frustumCullThresh), "카메라 뷰 외부에서 잔디를 컬링해야 하는 거리를 결정하는 수평 및 수직 뷰의 임계값입니다. 일반적으로 뷰 가장자리에서 잔디가 튀어나오는 것을 볼 수 없을 정도로 가능한 한 낮게 설정해야 합니다."},
            { nameof(GrassMesh.bakeDensity), "파라미터 맵을 사용하는 경우 밀도 채널을 기반으로만 잔디를 생성합니다. 이는 훨씬 더 효율적이지만, 유일한 단점은 런타임에 잔디 밀도를 동적으로 칠할 수 없다는 것입니다."},
            { nameof(GrassMesh.bakeData), "메모리 비용을 증가시키지만(37%), 컬러/파라미터/타입 맵을 사용할 때 더 효율적입니다.\n이 기능을 비활성화하고 맵을 사용하는 경우 해당 맵의 머티리얼에서 동적 맵 설정을 활성화해야 합니다.\n중요: 프러스텀 컬링은 현재 이 설정이 꺼져 있는 것과 호환되지 않습니다."},

            { nameof(GrassFlowRenderer.visualizeChunkBounds), "이게 정말 툴팁이 필요한가요? 음, 청크 경계는 블레이드 높이에 따라 자동으로 확장되어 이상한 각도로 컬링될 때 잔디가 튀어나오는 것을 방지합니다."},
            { nameof(GrassFlowRenderer.renderLayer), "잔디를 렌더링할 레이어입니다."},
            { nameof(GrassFlowRenderer.updateBuffers), "이 설정은 편집기에만 영향을 미칩니다. 스크립트가 다시 컴파일될 때 시각적 팝핑을 방지하므로 대부분의 경우 이 기능을 켜두는 것이 좋습니다." +
            "게임 성능을 더 정확하게 보려면 이 기능을 끌 수 있지만 실제로는 거의 차이가 없습니다."},
            { nameof(GrassFlowRenderer.useMaterialInstance), "true인 경우 렌더링할 머티리얼의 인스턴스가 생성됩니다. 여러 잔디에 동일한 머티리얼을 사용하고 싶지만 다른 텍스처 등을 사용하고 싶을 때 중요합니다."},
            { nameof(GrassFlowRenderer.terrainExpansion), "지형에서 잔디 청크를 확장할 양으로, 청크 가장자리의 아티팩트를 방지하는 데 도움이 됩니다. 보기 흉하지 않으면서 가능한 한 낮게 설정하는 것이 좋습니다."},
            { nameof(GrassFlowRenderer.normalizeMeshDensity), "소스 메시의 밀도가 매우 불균일하지 않은 한 이 설정을 활성화하지 마십시오. 처리 시간이 늘어나고 아마도 더 나쁜 결과를 낳을 것입니다." +
            "이 설정은 메시를 세분화하여 모든 삼각형의 크기를 가능한 한 동일하게 만들려고 시도하며 원래 모양은 정확하게 일치합니다." +
            "메시가 세분화되므로 증가된 밀도를 고려하여 GrassPerTri를 줄이는 것이 좋습니다."},

            
			//--------------------------------------------------------------------------------
			//인스펙터 관련---------------------------------------------------------------
			//--------------------------------------------------------------------------------
            { "refreshButton", "현재 모든 데이터를 해제/파괴하고 모든 것을 재설정합니다. 특정 항목을 변경한 후 잔디를 재설정하는 데 사용합니다." },
            { "globalRendererSettings", "이 설정은 모든 잔디 메시에 공유됩니다." },
            { "meshLodChunks", "LOD 컬링에 사용할 청크 수입니다. 각 청크까지의 거리는 렌더링될 잔디의 양을 제어합니다. MESH 모드에서는 일반적으로 Y 방향으로 하나 이상의 청크가 필요하지 않지만 매우 수직적인 지형이 있는 경우 유용할 수 있습니다. 청크가 너무 많으면 성능에 좋지 않지만 청크가 충분하지 않으면 잔디를 컬링할 때 보기 흉하고 뭉툭해 보이므로 보기 흉하지 않으면서 가능한 한 적은 수의 청크로 설정하십시오. (팁: 생각보다 많이 필요하지 않습니다.)" },
            { "terrainLodChunks", "LOD 컬링에 사용할 청크 수입니다. 각 청크까지의 거리는 렌더링될 잔디의 양을 제어합니다. 청크가 너무 많으면 성능에 좋지 않지만 청크가 충분하지 않으면 잔디를 컬링할 때 보기 흉하고 뭉툭해 보이므로 보기 흉하지 않으면서 가능한 한 적은 수의 청크로 설정하십시오. (팁: 생각보다 많이 필요하지 않습니다.)" },
            { "addCopyGrassMesh", "별도의 메시나 지형에 잔디를 렌더링할 수 있는 추가 GrassMesh를 추가하고 현재 선택된 GrassMesh에서 설정을 복사합니다." },
            { "addFromSelected", "계층 구조에서 선택한 개체에서 추가 GrassMesh를 추가하려고 시도하며 자동으로 트랜스폼과 메시를 채웁니다. 이 작업을 수행하려면 인스펙터를 잠근 다음 개체를 선택해야 할 수 있습니다." },
            { "revertChangesButton", "마지막으로 저장한 이후 디테일 맵에 대한 변경 사항을 버립니다. 맵은 프로젝트 에셋이 저장될 때마다(예: Ctrl+S) 저장됩니다. 되돌리기 단축키: Shift-R. 이 작업은 실행 취소/다시 실행을 지원해야 하며 아마도 작동할 것입니다." },
            { "bakeDensityToMeshButton", "파라미터 맵의 밀도 정보를 기반으로 새 메시를 만듭니다. 이 메시지를 사용하면 메시의 특정 부분에만 잔디를 더 효율적으로 렌더링할 수 있습니다. 결과 메시를 자동으로 적용하지 않습니다." },
            { "useBrushOpacity", "페인팅할 때 브러시 불투명도를 사용할지 여부입니다. 최대 강도로 잔디 유형을 페인팅할 때 브러시 불투명도가 밀도에 바람직하지 않은 영향을 미치는 아티팩트를 방지하려면 이 기능을 끄는 것이 이상적입니다." },
            { "grassTypeIndex", "잔디 텍스처 아틀라스의 인덱스입니다. 페인팅할 텍스처를 선택하기 위한 것입니다." },
            { "clampRange", "페인팅하는 동안 파라미터의 최소 및 최대 범위입니다. 이것은 본질적으로 덧셈이나 뺄셈이 아닌 설정된 값을 페인팅하는 데 사용할 수 있습니다." },
            { "raycastLayerMask", "이 마스크는 페인팅을 위해 지형/메시를 레이캐스팅할 때 사용됩니다. 이 마스크를 사용하여 지형이 있는 레이어에만 페인팅하고 차단 개체를 통해 페인팅하거나 그 반대로 할 수 있습니다." },
            { "paintContinuously", "꺼져 있으면 마우스를 움직여야 페인팅할 수 있고, 그렇지 않으면 마우스를 누르고 있는 동안 계속 페인팅됩니다." },
            { "useDeltaTimePaint", "켜져 있으면 브러시 강도가 델타 시간으로 곱해져 페인팅 강도가 프레임 속도에 독립적이 됩니다. 브러시를 스탬프처럼 사용하고 강도 1을 사용하여 한 번의 클릭으로 전체 브러시를 잔디에 적용하려는 경우 이 기능을 끄는 것이 유용합니다." },
            { "splatLayer", "잔디가 나타나는 위치를 마스킹하는 데 사용할 스플랫 텍스처 레이어의 인덱스입니다." },
            { "tolerance", "스플랫 맵 레이어를 적용할 때 불투명도 허용 오차를 제어합니다." },
            { "applyAdditive", "선택한 레이어를 기반으로 잔디를 추가하지만 기존 잔디는 제거하지 않습니다." },
            { "applySubtractive", "선택한 레이어를 기반으로 잔디를 제거하지만 스플랫 맵 외부의 잔디에는 영향을 미치지 않습니다." },
            { "applyReplace", "선택한 레이어를 기반으로 잔디를 추가하고 기존 잔디를 제거하고 덮어씁니다." },
            { "paintGrassColor", "클릭하여 색상을 칠합니다. 간단합니다." },
            { "paintGrassDensity", "클릭하여 잔디를 채웁니다. Shift+클릭하여 잔디를 지웁니다." },
            { "paintGrassHeight", "클릭하여 잔디를 높입니다. Shift+클릭하여 잔디를 낮춥니다." },
            { "paintGrassFlatness", "클릭하여 잔디를 평평하게 합니다. Shift+클릭하여 잔디를 평평하지 않게 합니다." },
            { "paintGrassWindStrength", "클릭하여 바람 강도를 높입니다. Shift+클릭하여 낮춥니다." },
            { "paintGrassType", "클릭하여 잔디 텍스처 아틀라스(사용하는 경우)에서 표시되는 텍스처를 칠합니다. Shift+클릭하여 첫 번째 텍스처를 칠합니다. 브러시 강도는 선택한 유형의 밀도를 제어합니다." },
            { "paintToolColor", "색상" },
            { "paintToolDensity", "밀도" },
            { "paintToolHeight", "높이" },
            { "paintToolFlatness", "평탄도" },
            { "paintToolWind", "바람 강도" },
            { "paintToolType", "잔디 유형" },
        };

        static Dictionary<string, string> _materialTooltips = new Dictionary<string, string>() {
            { "_Color", "풀잎의 기본 색상입니다." },
            { "bladeHeight", "풀잎의 전체 높이입니다." },
            { "bladeWidth", "풀잎의 전체 너비입니다." },
            { "flatTint", "납작해진 풀에 적용되는 색조입니다." },
            { "altCol", "변형에 사용되는 대체 색상입니다." },
            { "DISABLE_DECALS", "풀의 데칼을 비활성화합니다." },
            { "_ReceiveShadows", "풀이 그림자를 받을지 여부입니다." },
            { "specTint", "반사광 하이라이트의 색조입니다." },
            { "_Metallic", "풀의 금속성 외관을 제어합니다." },
            { "_Gloss", "반사광 반사를 위한 풀의 광택을 제어합니다." },
            { "_noiseScale2", "보조 바람 노이즈의 스케일입니다." },
            { "_noiseSpeed2", "보조 바람 노이즈의 속도입니다." },
            { "occMult", "차폐 맵의 강도입니다." },

            {"bladeOffset", "지형의 잔디 위치에 높이 오프셋을 추가하여 미세 조정에 유용할 수 있습니다."},
            {"bladeSharp", "잔디 블레이드의 날카로움을 제어합니다. 0은 완벽한 점, 1은 직사각형입니다."},
            {"seekSun", "잔디가 표면 법선에 얼마나 정렬되는지 제어합니다. 0은 완전히 정렬하고 1은 위를 향합니다."},
            {"topViewPush", "위에서 볼 때 잔디에 약간의 오프셋을 추가하여 아래를 볼 때 더 많은 깊이와 밀도를 제공하는 데 도움이 됩니다."},
            {"flatnessMult", "파라미터 맵의 평탄도 채널을 사용할 때 잔디가 얼마나 '평평하게' 밀리는지 제어합니다."},
            {"_BILLBOARD", "잔디가 항상 카메라를 향해야 하는지 여부입니다."},
            {"variance", "이 네 가지 값은 잔디가 특정 방식으로 얼마나 무작위화되는지를 제어합니다. 값은 X = 위치, Y = 높이, Z = 색상, W = 너비입니다."},

            //조명
            {"_ppLights","픽셀당 음영을 계산합니다. 약간 느리지만 사용자 정의 잔디 메시를 사용할 때만 눈에 띄게 나타나며 법선 매핑에 필요합니다."},
            {"_AO", "잔디 블레이드 바닥이 얼마나 어두운지 제어합니다. 0은 더 어둡고 1은 어두움이 없습니다."},
            {"ambientCO", "음영이 얼마나 어두울 수 있는지 제어합니다."},
            {"ambientCOShadow", "광원 그림자 강도 설정 외에도 수신된 그림자 강도를 추가로 조정할 수 있습니다."},
            {"edgeLight", "광원 방향이 잔디 블레이드에 가장자리일 때 추가되는 밝기의 강도를 제어합니다."},
            {"edgeLightSharp", "추가된 가장자리 조명 밝기의 선명도를 제어합니다."},
            {"blendNormal", "메시 법선을 지형 표면 법선과 혼합합니다. 이를 통해 음영 및 반사광을 더 잘 제어할 수 있습니다."},
            {"_GF_SPECULAR", "반사광 하이라이트를 활성화합니다. 최악의 경우 약 0.1ms의 약간의 성능 비용이 추가됩니다."},
            {"specSmooth", "반사광 하이라이트/반사를 위한 표면의 부드러움/흐림을 제어합니다."},
            {"specularMult", "반사광 하이라이트 강도의 배율입니다."},
            {"specHeight", "반사광 반사의 높이 조정으로, 잔디 바닥에 반사광 하이라이트가 없도록 조정하는 데 사용할 수 있습니다."},
            {"_GF_NORMAL_MAP", "법선 매핑을 활성화합니다. 적당한 성능 비용이 발생하며, 최악의 경우 약 1ms, 합리적인 경우 0.1ms입니다."},
            {"normalStrength", "법선 매핑 효과의 강도입니다."},
            {"bumpMap", "법선 매핑에 사용할 텍스처입니다."},

            //자체 그림자
            {"GF_SELF_SHADOW", "실제로 그림자를 렌더링하지 않고 잔디에 가짜 그림자를 추가하는 저렴한 기술을 활성화합니다. 기본적으로 주 광원의 관점에서 잔디 텍스처를 잔디에 다시 투영하여 작동합니다.\n" +
                "잔디 메시의 정점이 x/z 축에서 -0.5에서 0.5이고 y 축에서 0에서 1이라고 가정합니다.\n" +
                "컷아웃 텍스처와 잔디 카드를 사용하면 가장 잘 보입니다."},
            {"selfShadowWind", "자체 그림자가 바람에 의해 얼마나 변조되어 움직임을 부여하는지입니다."},
            {"selfShadowScaleOffset", "(x,y): 그림자 투영에 적용되는 스케일입니다.\n(z,w): 그림자 투영에 적용되는 오프셋입니다.\n이러한 값을 조정하여 특정 메시에 대한 배치를 미세 조정할 수 있습니다."},


            //LOD
            {"_ALPHA_TO_MASK", "활성화하면 셰이더에서 AlphaToMask가 켜집니다. 그리고 이것의 성능은 매우 복잡합니다. 때로는 활성화하면 잔디가 더 좋아 보이고 때로는 그렇지 않습니다."},
            {"widthLODscale", "카메라로부터의 거리가 증가함에 따라 블레이드의 너비가 어떻게 증가하는지 제어합니다. 이것은 눈에 띄지 않으면서 동일한 영역을 덮는 데 더 적은 잔디를 사용하는 데 도움이 됩니다."},
            {"_GF_USE_DITHER", "카메라로부터 특정 거리 내에서 또는 지연 모드에서 항상 LOD 전환을 더욱 숨기기 위해 잔디를 디더링합니다." +
                "대부분의 경우 이것을 켜면 더 좋아 보이지만 원하지 않을 수 있는 일부 아티팩트가 발생합니다." +
                "LOD 전환에서 특히 심한 팝핑을 발견하지 않는 한 이것을 끄십시오."},
            {"grassFade", "잔디가 시각적으로 사라지는 거리입니다. 참고: 이것은 lod 설정을 제어하지 않으며, GrassFlow 구성 요소에서 별도로 설정해야 하며 이 설정은 시각적인 것입니다."},
            {"grassFadeSharpness", "잔디 페이드의 선명도입니다."},
            {"_LOD_SCALING", "LOD 페이드인을 위해 잔디를 수직으로 스케일링합니다."},

            //바람
            {"windMult", "전체 바람 강도 배율입니다."},
            {"windTint", "바람이 강하게 영향을 미칠 때 잔디가 착색되는 색상이며, 알파는 강도를 제어합니다."},
            {"_noiseScale", "바람에 대한 노이즈 샘플링의 스케일이며, 일종의 바람 돌풍 크기를 제어합니다."},
            {"_noiseSpeed", "노이즈가 잔디를 가로질러 스크롤하여 바람 패턴을 변경하는 속도입니다. 바람 속도처럼 작동하지만 일치하도록 바람 강도를 조정해야 합니다."},
            {"windDir  ", "바람이 부는 방향이며, 이러한 값의 크기는 본질적으로 강도를 결정합니다."},
            {"windDir2", "바람 방향과 동일하지만 보조 바람 방향을 제어하며, 항상 한 방향으로 부는 대신 바람에 더 많은 다양성을 부여하는 데 도움이 됩니다."},
            
            //굽힘
            {"_MULTI_SEGMENT", "각 잔디 블레이드에 추가 세그먼트를 추가하여 바람이나 곡률로 인해 구부러질 수 있도록 합니다." +
                "세그먼트의 최소 및 최대 수는 GrassFlow/Shaders/GrassStructsVars.cginc 파일 상단의 숫자를 조정하여 변경할 수 있습니다." +
                "LOD 설정에 따라 잔디 세그먼트의 수는 거리에 따라 줄어듭니다."},
            {"bladeLateralCurve", "잔디의 자연스러운 굽힘 정도입니다."},
            {"bladeVerticalCurve", "잔디를 표면 쪽으로 약간 아래로 당깁니다."},
            {"bladeStiffness", "잔디가 바람/리플에 반응하여 얼마나 구부러지는지 제어합니다."},

            //맵 및 텍스처링
            {"_SEMI_TRANSPARENT", "알파가 있는 텍스처 사용을 활성화합니다."},
            {"alphaLock", "알파 클리핑을 적용하면서 잔디 텍스처 자체의 알파를 버립니다. 텍스처에 알파가 좋지 않거나 사용하고 싶지 않은 경우 유용할 수 있습니다."},
            {"alphaMult", "텍스처 알파에 대한 배율이며, 이를 높이면 텍스처의 알파가 충분히 선명하지 않은 경우 미세 조정할 수 있습니다."},
            {"alphaClip", "투명 텍스처의 클리핑이 얼마나 민감한지 제어합니다."},
            {"numTextures", "이것을 유형 맵 텍스처 아틀라스의 텍스처 수로 설정합니다. 유형 맵을 사용할 때만 사용됩니다."},
            {"textureAtlasScalingCutoff", "LOD 너비 스케일링이 꺼지는 유형 맵의 텍스처 인덱스입니다. 예를 들어 3으로 설정하면 스케일링은 아틀라스의 처음 세 텍스처에만 적용됩니다." +
                "유형 맵을 사용할 때만 사용됩니다."},
            {"_SpecMap", "지연 렌더링을 위한 스페큘러 맵입니다."},
            {"_OccMap", "지연 렌더링을 위한 오클루전 맵입니다."},
            {"_MainTex", "잔디 블레이드/쿼드를 자세히 설명하는 데 사용되는 텍스처입니다. 이것은 알파 클립에 사용되는 텍스처입니다. 유형 맵과 함께 사용되는 수평 텍스처 아틀라스일 수 있으며, 그렇다면 텍스처 수 속성도 설정해야 합니다."},
            {"colorMap", "GrassFlow용 컬러 맵입니다. 일반적으로 GrassFlowRenderer에서 설정하므로 무엇을 하고 있는지 모르는 한 건드리지 마십시오."},
            {"dhfParamMap", "GrassFlow용 파라미터 맵입니다. 일반적으로 GrassFlowRenderer에서 설정하므로 무엇을 하고 있는지 모르는 한 건드리지 마십시오."},
            {"typeMap", "GrassFlow용 유형 맵입니다. 일반적으로 GrassFlowRenderer에서 설정하므로 무엇을 하고 있는지 모르는 한 건드리지 마십시오."},


            //최적화
            {"_Cull", "렌더링을 위한 컬링 모드입니다. 메시에 양면 다각형이 있는 경우 '끄기'로 설정할 수 있습니다. 그렇지 않으면 가장 효율적이므로 대부분의 경우 후면 컬링으로 설정하는 것이 좋습니다."},
            {"MESH_COLORS", "메시에서 사용자 지정 정점 색상을 사용하여 바람에 대한 민감도를 결정할 수 있습니다. 색상의 빨간색 채널이 사용됩니다."},
            {"MESH_NORMALS", "메시의 법선을 사용할 수 있도록 합니다. 그렇지 않으면 지형의 법선이 사용됩니다. 간단한 잔디 카드의 경우 어쨌든 이 기능을 활성화하고 싶지 않을 수 있습니다."},
            {"MESH_UVS", "메시의 UV를 사용할 수 있도록 합니다. 텍스처링에 사용됩니다. 거의 항상 이 기능을 켜고 싶을 것이지만 잔디에 텍스처를 적용하지 않으면 끌 수도 있습니다."},
            {"MAP_COLOR", "런타임에 컬러 맵을 칠할 수 있도록 합니다. 그렇지 않으면 색상이 구워집니다. 이 기능을 활성화하면 셰이더에서 추가 텍스처 샘플이 사용됩니다. 모바일에서는 끄는 것이 가장 좋습니다."},
            {"MAP_PARAM", "런타임에 파라미터 맵을 칠할 수 있도록 합니다. 그렇지 않으면 값이 구워집니다. 이 기능을 활성화하면 셰이더에서 추가 텍스처 샘플이 사용됩니다. 모바일에서는 끄는 것이 가장 좋습니다."},
            {"MAP_TYPE",  "런타임에 유형 맵을 칠할 수 있도록 합니다. 그렇지 않으면 값이 구워집니다. 이 기능을 활성화하면 셰이더에서 추가 텍스처 샘플이 사용됩니다. 모바일에서는 끄는 것이 가장 좋습니다."},

            {"GRASS_RIPPLES",  "잔물결을 받을 수 있도록 합니다. 리플을 사용하지 않더라도 버퍼에서 읽어야 하므로 특히 모바일에서 비용이 많이들 수 있으므로 필요하지 않은 경우 끄는 것이 가장 좋습니다."},
            {"GRASS_FORCES",  "잔디에 여러 힘을 가할 수 있으며, 버퍼에서 읽어야 하므로 특히 모바일에서 비용이 많이들 수 있습니다. 꺼져 있으면 여전히 잔디에 한 가지 힘을 가할 수 있으며, 이는 주인공에게 가장 잘 사용됩니다."},
        };

        static readonly Dictionary<string, string> _messages = new Dictionary<string, string> {
            { "urpDetected", "URP 프로젝트가 감지되었습니다." },
            { "enableURPSupport", "URP 지원을 활성화하시겠습니까?" },
            { "hdrpDetected", "HDRP 프로젝트가 감지되었습니다." },
            { "enableHDRPSupport", "HDRP 지원을 활성화하시겠습니까?" },
            { "urpModeActive", "GrassFlow가 URP 모드입니다." },
            { "hdrpModeActive", "GrassFlow가 HDRP 모드입니다." },
            { "enjoyingGrassFlow", "⚠ GrassFlow를 즐기고 계신가요? : " },
            { "leaveReview", "리뷰 남기기" },
            { "dismiss", "닫기" },
            { "multipleRenderersWarning", "장면에 여러 GrassFlowRenderer가 있습니다!.\n하나의 렌더러만 사용하고 모든 지형/메시를 해당 단일 인스턴스에 할당해야 합니다.\n그렇지 않으면 성능 및 렌더링 문제가 발생합니다." },
            { "terrainTransformMissing", "지형 변환이 없습니다." },
            { "grassMeshMissing", "잔디 메시가 없습니다." },
            { "terrainMissing", "지형이 없습니다." },
            { "paintTextureMissing", "선택한 페인트 유형에 대한 텍스처가 없습니다." },
            { "grassMaterialMissing", "잔디 재질이 없습니다." },
            { "customMeshMissing", "렌더러 구성 요소에 사용자 정의 메시가 설정되지 않았습니다." },
            { "undoRevertMaps", "GrassFlow 맵 되돌리기" },
            { "undoChangeVariable", "GrassFlow 변수 변경" },
            { "undoAddGrassMesh", "잔디 메시 추가" },
            { "undoAddFromSelection", "선택 항목에서 잔디 메시 추가" },
            { "undoDeleteGrassMesh", "잔디 메시 삭제" },
            { "undoSelectGrassMesh", "GrassFlow 메시 선택" },
            { "undoSetDetailMap", "GrassFlow 디테일 맵 설정" },
            { "undoChangeBrush", "GrassFlow 브러시 변경" },
            { "missingTerrainLayers", "지형에 누락된 레이어가 있습니다! 지형 개체에 문제가 있는지 확인하십시오." },
            { "noSplatLayers", "지형에 스플랫 레이어가 없습니다." },
            { "assignTerrainObject", "설정에서 지형 개체를 할당하십시오." },
            { "undoPaint", "GrassFlow 페인트" },
            { "errorPaintTextureMissing", "GrassFlow: 선택한 페인트 모드에 대한 텍스처가 설정되지 않았습니다." },
            { "errorCantSaveMapNoFile", "텍스처 맵을 저장할 수 없습니다! 아마도 파일이 없기 때문일 것입니다." },
            { "errorMapNotPng", "디테일 맵은 .png 형식이어야 합니다!" },
            { "saveDialogTitle", "GrassFlow" },
            { "saveDialogMessage", "GrassFlow 디테일 맵이 수정되었습니다.\n변경 사항을 저장하시겠습니까?\n\n이 작업은 되돌릴 수 없습니다." },
            { "saveDialogYes", "예" },
            { "saveDialogNo", "아니요" },
            { "undoChangePaintTool", "GrassFlow 페인트 도구 변경" },

            
			//--------------------------------------------------------------------------------
			//기타---------------------------------------------------------------
			//--------------------------------------------------------------------------------
			{ "languageSelectorTitle", "Grassflow 언어 선택기" },
            { "selectLanguagePrompt", "언어를 선택하십시오:" },
            { "languageLabel", "언어:" },
            { "confirmSelectionButton", "선택 확인" },
        };
    }
}