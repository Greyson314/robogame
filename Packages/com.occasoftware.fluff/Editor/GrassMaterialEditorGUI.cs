using UnityEngine;
using UnityEditor;
using System;

namespace OccaSoftware.Fluff.Editor
{
    public class GrassMaterialEditorGUI : ShaderGUI
    {
        Material t;

        public override void OnGUI(MaterialEditor e, MaterialProperty[] properties)
        {
            t = e.target as Material;

            // Color
            MaterialProperty _MainTex = FindProperty("_MainTex", properties);
            MaterialProperty _GroundTex = FindProperty("_GroundTex", properties);
            MaterialProperty _BaseColor = FindProperty("_BaseColor", properties);
            MaterialProperty _TopColor = FindProperty("_TopColor", properties);

            MaterialProperty _MaximumHeight = FindProperty("_MaximumHeight", properties);
            MaterialProperty _FinsEnabled = FindProperty("_FinsEnabled", properties);
            MaterialProperty _ShellsEnabled = FindProperty("_ShellsEnabled", properties);
            MaterialProperty _ShellCount = FindProperty("_ShellCount", properties);

            MaterialProperty _GrassCoverageTexture = FindProperty("_GrassCoverageTexture", properties);

            MaterialProperty _ShapeNoiseTexture = FindProperty("_ShapeNoiseTexture", properties);
            MaterialProperty _ShapeNoiseStrength = FindProperty("_ShapeNoiseStrength", properties);
            MaterialProperty _ShapeNoiseScale = FindProperty("_ShapeNoiseScale", properties);

            MaterialProperty _DetailNoiseTexture = FindProperty("_DetailNoiseTexture", properties);
            MaterialProperty _DetailNoiseStrength = FindProperty("_DetailNoiseStrength", properties);
            MaterialProperty _DetailNoiseScale = FindProperty("_DetailNoiseScale", properties);

            MaterialProperty _GrassTintColor = FindProperty("_GrassTintColor", properties);
            MaterialProperty _GrassInteractivityCutAmount = FindProperty("_GrassInteractivityCutAmount", properties);

            MaterialProperty _WindColor = FindProperty("_WindColor", properties);
            MaterialProperty _WindDirection = FindProperty("_WindDirection", properties);

            MaterialProperty _WindTexture = FindProperty("_WindTexture", properties);

            MaterialProperty _WindMainStrength = FindProperty("_WindMainStrength", properties);

            MaterialProperty _WindPulseStrength = FindProperty("_WindPulseStrength", properties);
            MaterialProperty _WindPulseFrequency = FindProperty("_WindPulseFrequency", properties);

            MaterialProperty _WindTurbulenceStrength = FindProperty("_WindTurbulenceStrength", properties);
            MaterialProperty _WindTurbulenceScale = FindProperty("_WindTurbulenceScale", properties);
            MaterialProperty _WindTurbulenceSpeed = FindProperty("_WindTurbulenceSpeed", properties);

            MaterialProperty _GrassDirectionMap = FindProperty("_GrassDirectionMap", properties);
            MaterialProperty _GrassDirectionStrength = FindProperty("_GrassDirectionStrength", properties);
            MaterialProperty _SurfaceNormalExclusionEnabled = FindProperty("_SurfaceNormalExclusionEnabled", properties);
            MaterialProperty _SurfaceNormalPower = FindProperty("_SurfaceNormalPower", properties);

            MaterialProperty _FadeStartDistance = FindProperty("_FadeStartDistance", properties);
            MaterialProperty _MaximumDistance = FindProperty("_MaximumDistance", properties);

            MaterialProperty _TextureSamplingMethod = FindProperty("_TextureSamplingMethod", properties);
            MaterialProperty _WorldScale = FindProperty("_WorldScale", properties);

            MaterialProperty _CastShadowsEnabled = FindProperty("_CastShadowsEnabled", properties);
            MaterialProperty _ReceiveShadowsEnabled = FindProperty("_ReceiveShadowsEnabled", properties);
            MaterialProperty _ReceiveAmbientLightingEnabled = FindProperty("_ReceiveAmbientLightingEnabled", properties);
            MaterialProperty _ReceiveFogEnabled = FindProperty("_ReceiveFogEnabled", properties);
            MaterialProperty _ReceiveDirectLightingEnabled = FindProperty("_ReceiveDirectLightingEnabled", properties);

            DrawColor();
            DrawGeometry();
            DrawCoverage();
            DrawNoise();
            DrawInteractivity();
            DrawWind();
            DrawDirectionMapping();
            DrawRenderSettings();
            DrawLightingSettings();

            void DrawColor()
            {
                EditorGUILayout.LabelField("Color", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                e.TexturePropertyWithHDRColor(new GUIContent("Grass", "Set the grass texture and color tint."), _MainTex, _TopColor, false);
                e.TexturePropertyWithHDRColor(new GUIContent("Ground", "Set the ground texture and color tint."), _GroundTex, _BaseColor, false);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            void DrawGeometry()
            {
                EditorGUILayout.LabelField("Geometry", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                e.ShaderProperty(_ShellCount, new GUIContent("Shell Count", "Number of grass layers."));
                e.ShaderProperty(_MaximumHeight, new GUIContent("Maximum Height", "Maximum height of the grass layer."));
                e.ShaderProperty(
                    _FinsEnabled,
                    new GUIContent(
                        "Fins Enabled",
                        "Fins are vertical grass slices, like billboards, used to help the grass illusion. Disable for performance or visual preference."
                    )
                );
                e.ShaderProperty(
                    _ShellsEnabled,
                    new GUIContent(
                        "Shells Enabled",
                        "Shells are horizontal grass layers, like layers of a cake, used to render the grass shape. Typically you will keep this enabled, but may disable for debugging purposes"
                    )
                );
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            void DrawCoverage()
            {
                EditorGUILayout.LabelField("Coverage", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                e.TexturePropertySingleLine(
                    new GUIContent(
                        "Coverage",
                        "Use the coverage map to selectively exclude grass in certain areas of the mesh. Where the texture is black, grass will not render. Only uses R channel."
                    ),
                    _GrassCoverageTexture
                );
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            void DrawConditionalTextureProperty(GUIContent content, MaterialProperty a, MaterialProperty b)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = a.hasMixedValue || b.hasMixedValue;

                if (a.textureValue == null)
                    b = null;

                e.TexturePropertySingleLine(content, a, b);
                if (EditorGUI.EndChangeCheck())
                {
                    b.floatValue = Mathf.Max(0, b.floatValue);
                }
                EditorGUI.showMixedValue = false;
            }

            void DrawNoise()
            {
                EditorGUILayout.LabelField("Noise", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                DrawConditionalTextureProperty(
                    new GUIContent(
                        "Shape Noise",
                        "Sets the overall shape of the grass volume. Use a lower frequency texture here. Only uses R channel."
                    ),
                    _ShapeNoiseTexture,
                    _ShapeNoiseStrength
                );
                if (_ShapeNoiseTexture.textureValue != null && _ShapeNoiseStrength.floatValue > 0)
                {
                    EditorGUI.indentLevel++;
                    DrawFloatWithMinValue(new GUIContent("Scale"), _ShapeNoiseScale, 0);
                    EditorGUI.indentLevel--;
                }
                DrawConditionalTextureProperty(
                    new GUIContent("Detail Noise", "Sets the grass detail shape. Use a higher frequency texture here. Only uses R channel."),
                    _DetailNoiseTexture,
                    _DetailNoiseStrength
                );
                if (_DetailNoiseTexture.textureValue != null && _DetailNoiseStrength.floatValue > 0)
                {
                    EditorGUI.indentLevel++;
                    DrawFloatWithMinValue(new GUIContent("Scale"), _DetailNoiseScale, 0);
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            void DrawInteractivity()
            {
                EditorGUILayout.LabelField(
                    new GUIContent("Interactivity", "Configure per-material grass interactivity settings."),
                    EditorStyles.boldLabel
                );
                EditorGUI.indentLevel++;
                e.ShaderProperty(
                    _GrassTintColor,
                    new GUIContent("Grass Tint Color", "Color to tint the grass based on the Blue channel of the grass Interactivity map.")
                );
                e.ShaderProperty(
                    _GrassInteractivityCutAmount,
                    new GUIContent(
                        "Grass Press Amount",
                        "Maximum height reduction for grass pressed via Green channel of the grass Interactivity map."
                    )
                );
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            void DrawVector2(GUIContent content, MaterialProperty a)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = a.hasMixedValue;
                Vector4 v = EditorGUILayout.Vector2Field(content, a.vectorValue);
                if (EditorGUI.EndChangeCheck())
                {
                    a.vectorValue = v;
                }
                EditorGUI.showMixedValue = false;
            }

            void DrawFloatWithMinValue(GUIContent content, MaterialProperty a, float min)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = a.hasMixedValue;
                e.ShaderProperty(a, content);

                if (EditorGUI.EndChangeCheck())
                    a.floatValue = Mathf.Max(min, a.floatValue);

                EditorGUI.showMixedValue = false;
            }

            void DrawFloatWithClampedValue(GUIContent content, MaterialProperty a, float min, float max)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = a.hasMixedValue;
                e.ShaderProperty(a, content);

                if (EditorGUI.EndChangeCheck())
                    a.floatValue = Mathf.Clamp(a.floatValue, min, max);

                EditorGUI.showMixedValue = false;
            }

            void DrawTextureProperty(MaterialProperty p, GUIContent c)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = p.hasMixedValue;
                Texture2D t = (Texture2D)
                    EditorGUILayout.ObjectField(c, p.textureValue, typeof(Texture2D), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (EditorGUI.EndChangeCheck())
                {
                    p.textureValue = t;
                }
                EditorGUI.showMixedValue = false;
            }

            void DrawWind()
            {
                EditorGUILayout.LabelField(new GUIContent("Wind", "Customize the wind properties."), EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                DrawVector2(
                    new GUIContent(
                        "Direction",
                        "Sets the overall direction that wind blows. Corresponds to [x, y] values in World or UV space depending on your Texture Sampling Method."
                    ),
                    _WindDirection
                );
                e.ShaderProperty(
                    _WindColor,
                    new GUIContent(
                        "Wind Lighting",
                        "Treats high areas of wind as receiving additional lighting (calculated as the relative wind power at the fragment as a ratio of the sum of the Wind Pulse Strength + Wind Turbulent Strength). Set to black to disable. Nice to highlight windy areas."
                    )
                );

                DrawFloatWithMinValue(
                    new GUIContent("Main Wind Strength", "Hints the grass towards the wind direction. Static."),
                    _WindMainStrength,
                    0
                );

                DrawFloatWithMinValue(new GUIContent("Pulse Strength", "The maximum strength of periodic wind pulses."), _WindPulseStrength, 0);
                if (_WindPulseStrength.floatValue > 0)
                {
                    EditorGUI.indentLevel++;
                    DrawFloatWithMinValue(new GUIContent("Pulse Frequency", "The frequency of periodic wind pulses."), _WindPulseFrequency, 0);
                    EditorGUI.indentLevel--;
                }

                DrawTextureProperty(_WindTexture, new GUIContent("Turbulence", "The texture to use for sampling turbulent wind noise."));

                if (_WindTexture.textureValue != null)
                {
                    EditorGUI.indentLevel++;
                    DrawFloatWithMinValue(new GUIContent("Strength", "The maximum strength of the turbulent noise."), _WindTurbulenceStrength, 0);
                    if (_WindTurbulenceStrength.floatValue > 0)
                    {
                        EditorGUI.indentLevel++;
                        DrawFloatWithMinValue(
                            new GUIContent("Speed", "The speed at which the turbulent noise will move across the grass."),
                            _WindTurbulenceSpeed,
                            0
                        );
                        DrawFloatWithMinValue(
                            new GUIContent(
                                "Scale",
                                "The implied scale of the wind texture. A larger scale means the texture is distributed over a larger area."
                            ),
                            _WindTurbulenceScale,
                            0
                        );
                        EditorGUI.indentLevel--;
                    }

                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            void DrawDirectionMapping()
            {
                EditorGUILayout.LabelField("Direction", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                DrawConditionalTextureProperty(
                    new GUIContent(
                        "Direction",
                        "Normally, all grass would point directly up. The direction map lets you offset the direction of the grass. Uses the RG components of the texture to influence the XY direction of the grass."
                    ),
                    _GrassDirectionMap,
                    _GrassDirectionStrength
                );
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            void DrawEnumProperty(Enum e, MaterialProperty p, GUIContent c)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = p.hasMixedValue;
                var v = EditorGUILayout.EnumPopup(c, e);
                if (EditorGUI.EndChangeCheck())
                {
                    p.floatValue = Convert.ToInt32(v);
                }
                EditorGUI.showMixedValue = false;
            }

            void DrawRenderSettings()
            {
                EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;

                DrawFloatWithClampedValue(
                    new GUIContent(
                        "Fade Out Start Distance",
                        "The distance at which the grass will begin to fade out. As the grass fades out, the height will gradually reduce to the maximum distance."
                    ),
                    _FadeStartDistance,
                    0,
                    _MaximumDistance.floatValue - 0.5f
                );
                DrawFloatWithMinValue(
                    new GUIContent("Maximum Rendering Distance", "Grass will not render from this distance onwards."),
                    _MaximumDistance,
                    _FadeStartDistance.floatValue + 0.5f
                );

                e.ShaderProperty(
                    _SurfaceNormalExclusionEnabled,
                    new GUIContent(
                        "Fade by Surface Normal",
                        "Enable this setting if you want the grass to be faded out at steep or inverted angles in world space. When disabled, grass can grow upside down or out of walls."
                    )
                );
                if (_SurfaceNormalExclusionEnabled.floatValue == 1.0f)
                {
                    EditorGUI.indentLevel++;
                    DrawFloatWithMinValue(
                        new GUIContent("Surface Normal Power", "The strength of the surface normal fading."),
                        _SurfaceNormalPower,
                        0.1f
                    );
                    EditorGUI.indentLevel--;
                }

                DrawEnumProperty(
                    (SamplingMethod)_TextureSamplingMethod.floatValue,
                    _TextureSamplingMethod,
                    new GUIContent(
                        "Sampling Method",
                        "The World Space sampling method is best for most use cases. The UV Space sampling method is best for spherical objects or when you want grass to grow horizontally or upside-down."
                    )
                );
                if ((SamplingMethod)_TextureSamplingMethod.floatValue == SamplingMethod.WorldSpace)
                {
                    EditorGUI.indentLevel++;
                    e.ShaderProperty(
                        _WorldScale,
                        new GUIContent(
                            "World Scale",
                            "Scales all textures in world space. A larger value means the textures are distributed over a larger area."
                        )
                    );
                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            void DrawLightingSettings()
            {
                EditorGUILayout.LabelField(new GUIContent("Lighting", "Configure lighting settings."), EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                e.ShaderProperty(_CastShadowsEnabled, new GUIContent("Cast Shadows", "Should this mesh cast shadows on other objects?"));
                t.SetShaderPassEnabled("ShadowCaster", _CastShadowsEnabled.floatValue == 1.0f);
                e.ShaderProperty(_ReceiveShadowsEnabled, new GUIContent("Receive Shadows", "Should the grass receive cast shadows?"));
                e.ShaderProperty(
                    _ReceiveDirectLightingEnabled,
                    new GUIContent("Receive Direct Lighting", "Should the grass receive direct lighting?")
                );
                e.ShaderProperty(
                    _ReceiveAmbientLightingEnabled,
                    new GUIContent("Receive Ambient Lighting", "Should the grass receive ambient scene lighting? Calculated via Spherical Harmonics.")
                );
                e.ShaderProperty(_ReceiveFogEnabled, new GUIContent("Receive Fog", "Should the grass receive Unity's built-in fog?"));

                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }
        }

        private enum SamplingMethod
        {
            UVSpace,
            WorldSpace
        }
    }
}
