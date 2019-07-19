using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Experimental.Rendering.Universal
{
    internal static class RendererLighting
    {
        static readonly ShaderTagId k_NormalsRenderingPassName = new ShaderTagId("NormalsRendering");
        static readonly Color k_NormalClearColor = new Color(0.5f, 0.5f, 1.0f, 1.0f);
        static readonly string k_SpriteLightKeyword = "SPRITE_LIGHT";
        static readonly string k_UsePointLightCookiesKeyword = "USE_POINT_LIGHT_COOKIES";
        static readonly string k_LightQualityFastKeyword = "LIGHT_QUALITY_FAST";
        static readonly string k_UseNormalMap = "USE_NORMAL_MAP";
        static readonly string k_UseAdditiveBlendingKeyword = "USE_ADDITIVE_BLENDING";
        const int k_NumberOfLightMaterials = 1 << 5 + 3;  // 5 keywords +  volume bit, shape bit

        static readonly string[] k_UseBlendStyleKeywords =
        {
            "USE_SHAPE_LIGHT_TYPE_0",
            "USE_SHAPE_LIGHT_TYPE_1",
            "USE_SHAPE_LIGHT_TYPE_2",
            "USE_SHAPE_LIGHT_TYPE_3"
        };

        static Renderer2DData s_RendererData;
        static Light2DBlendStyle[] s_BlendStyles;
        static RenderTargetHandle[] s_LightRenderTargets;
        static bool[] s_LightRenderTargetsDirty;
        static RenderTargetHandle s_ShadowsRenderTarget;
        static RenderTargetHandle s_NormalsTarget;
        static Texture s_LightLookupTexture;
        static Texture s_FalloffLookupTexture;
        static Material[] s_LightMaterials;
        static Material[] s_ShadowMaterials;
        static Material s_RemoveSelfShadowMaterial;

        static public void Setup(Renderer2DData rendererData)
        {
            s_RendererData = rendererData;
            s_BlendStyles = rendererData.lightBlendStyles;

            if (s_LightRenderTargets == null)
            {
                s_LightRenderTargets = new RenderTargetHandle[s_BlendStyles.Length];
                s_LightRenderTargets[0].Init("_ShapeLightTexture0");
                s_LightRenderTargets[1].Init("_ShapeLightTexture1");
                s_LightRenderTargets[2].Init("_ShapeLightTexture2");
                s_LightRenderTargets[3].Init("_ShapeLightTexture3");

                s_LightRenderTargetsDirty = new bool[s_BlendStyles.Length];
            }

            if (s_NormalsTarget.id == 0)
                s_NormalsTarget.Init("_NormalMap");

            // The array size should be determined by the number of 'feature bit' the material index has. See GetLightMaterialIndex().
            // Not all slots must be filled because certain combinations of the feature bits don't make sense (e.g. sprite bit on + shape bit off).
            if (s_LightMaterials == null)
                s_LightMaterials = new Material[k_NumberOfLightMaterials];
        }

        static public void CreateRenderTextures(CommandBuffer cmd, Camera camera)
        {
            var renderTextureFormatToUse = RenderTextureFormat.ARGB32;
            if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float))
                renderTextureFormatToUse = RenderTextureFormat.RGB111110Float;
            else if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                renderTextureFormatToUse = RenderTextureFormat.ARGBHalf;

            RenderTextureDescriptor descriptor = new RenderTextureDescriptor();
            descriptor.colorFormat = renderTextureFormatToUse;
            descriptor.sRGB = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.dimension = TextureDimension.Tex2D;

            descriptor.width = (int)(camera.pixelWidth/128);
            descriptor.height = (int)(camera.pixelHeight/128);

            cmd.GetTemporaryRT(s_ShadowsRenderTarget.id, descriptor, FilterMode.Bilinear); // Needs to be switch to R8 color format...


            descriptor.width = (int)(camera.pixelWidth);
            descriptor.height = (int)(camera.pixelHeight);
            cmd.GetTemporaryRT(s_NormalsTarget.id, descriptor, FilterMode.Bilinear);

            
            for (int i = 0; i < s_BlendStyles.Length; ++i)
            {
                if (!s_BlendStyles[i].enabled)
                    continue;

                float renderTextureScale = Mathf.Clamp(s_BlendStyles[i].renderTextureScale, 0.01f, 1.0f);
                descriptor.width = (int)(camera.pixelWidth * renderTextureScale);
                descriptor.height = (int)(camera.pixelHeight * renderTextureScale);
                cmd.GetTemporaryRT(s_LightRenderTargets[i].id, descriptor, FilterMode.Bilinear);
                s_LightRenderTargetsDirty[i] = true;
            }


            
        }

        static public void ReleaseRenderTextures(CommandBuffer cmd)
        {
            for (int i = 0; i < s_BlendStyles.Length; ++i)
            {
                if (!s_BlendStyles[i].enabled)
                    continue;

                cmd.ReleaseTemporaryRT(s_LightRenderTargets[i].id);
            }

            cmd.ReleaseTemporaryRT(s_NormalsTarget.id);
        }

        static private bool RenderLightSet(Camera camera, int blendStyleIndex, CommandBuffer cmdBuffer, int layerToRender, List<Light2D> lights, RenderTargetIdentifier currentRTIdentifier)
        {
            bool renderedAnyLight = false;

            Material removeSelfShadowMaterial = GetRemoveSelfShadowMaterial();

            foreach (var light in lights)
            {
                if (light != null && light.lightType != Light2D.LightType.Global && light.blendStyleIndex == blendStyleIndex && light.IsLitLayer(layerToRender) && light.IsLightVisible(camera))
                {

                    // Render light's shadows
                    //if (light.castsShadows)
                    //cmdBuffer.SetRenderTarget(s_ShadowsRenderTarget.Identifier()); // This isn't efficient if this light doesn't cast shadow.
                    cmdBuffer.SetRenderTarget(Renderer2DData.s_RenderTexture);
                    cmdBuffer.ClearRenderTarget(true, true, Color.black);
                    cmdBuffer.SetGlobalTexture("_ShadowTex", Renderer2DData.s_RenderTexture);
                    cmdBuffer.SetGlobalFloat("_ShadowIntensity", 1-light.shadowIntensity);

                    if (light.shadowIntensity > 0)
                    {
                        BoundingSphere lightBounds = light.GetBoundingSphere(); // Gets the local bounding sphere...
                        Vector3 lightCenterWS = lightBounds.position;
                        float lightRadiusSq = lightBounds.radius * lightBounds.radius;

                        cmdBuffer.SetGlobalVector("_LightPos", light.transform.position);
                        cmdBuffer.SetGlobalFloat("_LightRadius", lightBounds.radius);


                        byte incrementedStencilGroup = 0;
                        Material shadowMaterial = GetShadowMaterial(0);
                        int previousShadowCasterGroup = int.MinValue;
                        List<ShadowCaster2D> shadowCasters = ShadowCaster2D.shadowCasters;
                        if (shadowCasters != null && shadowCasters.Count > 0)
                        {
                            
                            for(int i=0;i<shadowCasters.Count;i++)
                            {
                                ShadowCaster2D shadowCaster = shadowCasters[i];

                                float shadowRadiusSq = shadowCaster.radius * shadowCaster.radius;
                                Vector3 deltaPos = lightCenterWS - shadowCaster.transform.position;
                                float sqDist = deltaPos.x * deltaPos.x + deltaPos.y * deltaPos.y;

                                // Check to see if our shadow caster is inside the lights bounds...
                                if (sqDist < (shadowRadiusSq + lightRadiusSq))
                                {
                                    if (LightUtility.CheckForChange(shadowCaster.m_Group, ref previousShadowCasterGroup))
                                    {
                                        shadowMaterial = GetShadowMaterial(incrementedStencilGroup);
                                        incrementedStencilGroup++;
                                    }

                                    cmdBuffer.DrawMesh(shadowCaster.mesh, Matrix4x4.TRS(shadowCaster.transform.position, shadowCaster.transform.rotation, shadowCaster.transform.lossyScale), new Material(shadowMaterial));
                                    cmdBuffer.DrawMesh(shadowCaster.mesh, Matrix4x4.TRS(shadowCaster.transform.position, shadowCaster.transform.rotation, shadowCaster.transform.lossyScale), removeSelfShadowMaterial);
                                }
                            }
                        }
                    }
                    else
                    {
                        //cmdBuffer.SetGlobalTexture("_ShadowTex", s_WhitePixel);
                    }

                    cmdBuffer.SetRenderTarget(currentRTIdentifier); // This isn't efficient if this light doesn't cast shadow.

                    // Render light
                    Material lightMaterial = GetLightMaterial(light, false);
                    if (lightMaterial != null)
                    {
                        Mesh lightMesh = light.GetMesh();
                        if (lightMesh != null)
                        {
                            if (!renderedAnyLight)
                                renderedAnyLight = true;

                            if (light.lightType == Light2D.LightType.Sprite && light.lightCookieSprite != null && light.lightCookieSprite.texture != null)
                                cmdBuffer.SetGlobalTexture("_CookieTex", light.lightCookieSprite.texture);

                            cmdBuffer.SetGlobalFloat("_FalloffIntensity", light.falloffIntensity);
                            cmdBuffer.SetGlobalFloat("_FalloffDistance", light.shapeLightFalloffSize);
                            cmdBuffer.SetGlobalVector("_FalloffOffset", light.shapeLightFalloffOffset);
                            cmdBuffer.SetGlobalColor("_LightColor", light.intensity * light.color);
                            cmdBuffer.SetGlobalFloat("_VolumeOpacity", light.volumeOpacity);

                            if(light.useNormalMap || light.lightType == Light2D.LightType.Point)
                                RendererLighting.SetPointLightShaderGlobals(cmdBuffer, light);

                            // Light code could be combined...
                            if (light.lightType == Light2D.LightType.Parametric || light.lightType == Light2D.LightType.Freeform || light.lightType == Light2D.LightType.Sprite)
                            {
                                cmdBuffer.DrawMesh(lightMesh, light.transform.localToWorldMatrix, lightMaterial);
                            }
                            else if(light.lightType == Light2D.LightType.Point)
                            {
                                Vector3 scale = new Vector3(light.pointLightOuterRadius, light.pointLightOuterRadius, light.pointLightOuterRadius);
                                Matrix4x4 matrix = Matrix4x4.TRS(light.transform.position, Quaternion.identity, scale);
                                cmdBuffer.DrawMesh(lightMesh, matrix, lightMaterial);
                            }
                        }
                    }
                }
            }

            return renderedAnyLight;
        }

        static private void RenderLightVolumeSet(Camera camera, int blendStyleIndex, CommandBuffer cmdBuffer, int layerToRender, RenderTargetIdentifier renderTexture, List<Light2D> lights)
        {
            if (lights.Count > 0)
            {
                for (int i = 0; i < lights.Count; i++)
                {
                    Light2D light = lights[i];

                    int topMostLayer = light.GetTopMostLitLayer();
                    if (layerToRender == topMostLayer)
                    {
                        if (light != null && light.lightType != Light2D.LightType.Global && light.volumeOpacity > 0.0f && light.blendStyleIndex == blendStyleIndex && light.IsLitLayer(layerToRender) && light.IsLightVisible(camera))
                        {
                            Material lightVolumeMaterial = GetLightMaterial(light, true);
                            if (lightVolumeMaterial != null)
                            {
                                Mesh lightMesh = light.GetMesh();
                                if (lightMesh != null)
                                {
                                    if (light.lightType == Light2D.LightType.Sprite && light.lightCookieSprite != null && light.lightCookieSprite.texture != null)
                                        cmdBuffer.SetGlobalTexture("_CookieTex", light.lightCookieSprite.texture);

                                    cmdBuffer.SetGlobalFloat("_FalloffIntensity", light.falloffIntensity);
                                    cmdBuffer.SetGlobalFloat("_FalloffDistance", light.shapeLightFalloffSize);
                                    cmdBuffer.SetGlobalVector("_FalloffOffset", light.shapeLightFalloffOffset);
                                    cmdBuffer.SetGlobalColor("_LightColor", light.intensity * light.color);
                                    cmdBuffer.SetGlobalFloat("_VolumeOpacity", light.volumeOpacity);

                                    // Is this needed
                                    if (light.useNormalMap || light.lightType == Light2D.LightType.Point)
                                        RendererLighting.SetPointLightShaderGlobals(cmdBuffer, light);

                                    // Could be combined...
                                    if (light.lightType == Light2D.LightType.Parametric || light.lightType == Light2D.LightType.Freeform || light.lightType == Light2D.LightType.Sprite)
                                    {
                                        cmdBuffer.DrawMesh(lightMesh, light.transform.localToWorldMatrix, lightVolumeMaterial);
                                    }
                                    else if (light.lightType == Light2D.LightType.Point)
                                    {
                                        Vector3 scale = new Vector3(light.pointLightOuterRadius, light.pointLightOuterRadius, light.pointLightOuterRadius);
                                        Matrix4x4 matrix = Matrix4x4.TRS(light.transform.position, Quaternion.identity, scale);
                                        cmdBuffer.DrawMesh(lightMesh, matrix, lightVolumeMaterial);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        static public void SetShapeLightShaderGlobals(CommandBuffer cmdBuffer)
        {
            for (int i = 0; i < s_BlendStyles.Length; ++i)
            {

                if (i >= k_UseBlendStyleKeywords.Length)
                    break;

                string keyword = k_UseBlendStyleKeywords[i];
                if (!s_BlendStyles[i].enabled)
                {
                    cmdBuffer.DisableShaderKeyword(keyword);
                    continue;
                } 
                else
                {
                    cmdBuffer.EnableShaderKeyword(keyword);
                    cmdBuffer.SetGlobalVector("_ShapeLightBlendFactors" + i, s_BlendStyles[i].blendFactors);
                    cmdBuffer.SetGlobalVector("_ShapeLightMaskFilter" + i, s_BlendStyles[i].maskTextureChannelFilter.mask);
                    cmdBuffer.SetGlobalVector("_ShapeLightInvertedFilter" + i, s_BlendStyles[i].maskTextureChannelFilter.inverted);
                }
            }

            cmdBuffer.SetGlobalTexture("_FalloffLookup", GetFalloffLookupTexture());
        }

        static Texture GetLightLookupTexture()
        {
            if (s_LightLookupTexture == null)
                s_LightLookupTexture = Light2DLookupTexture.CreatePointLightLookupTexture();

            return s_LightLookupTexture;
        }

        static Texture GetFalloffLookupTexture()
        {
            if (s_FalloffLookupTexture == null)
                s_FalloffLookupTexture = Light2DLookupTexture.CreateFalloffLookupTexture();

            return s_FalloffLookupTexture;
        }

        static public float GetNormalizedInnerRadius(Light2D light)
        {
            return light.pointLightInnerRadius / light.pointLightOuterRadius;
        }

        static public float GetNormalizedAngle(float angle)
        {
            return (angle / 360.0f);
        }

        static public void GetScaledLightInvMatrix(Light2D light, out Matrix4x4 retMatrix, bool includeRotation)
        {
            float outerRadius = light.pointLightOuterRadius;
            //Vector3 lightScale = light.transform.lossyScale;
            Vector3 lightScale = Vector3.one;
            Vector3 outerRadiusScale = new Vector3(lightScale.x * outerRadius, lightScale.y * outerRadius, lightScale.z * outerRadius);

            Quaternion rotation;
            if (includeRotation)
                rotation = light.transform.rotation;
            else
                rotation = Quaternion.identity;

            Matrix4x4 scaledLightMat = Matrix4x4.TRS(light.transform.position, rotation, outerRadiusScale);
            retMatrix = Matrix4x4.Inverse(scaledLightMat);
        }

        static public void SetPointLightShaderGlobals(CommandBuffer cmdBuffer, Light2D light)
        {
            // This is used for the lookup texture
            Matrix4x4 lightInverseMatrix;
            Matrix4x4 lightNoRotInverseMatrix;
            GetScaledLightInvMatrix(light, out lightInverseMatrix, true);
            GetScaledLightInvMatrix(light, out lightNoRotInverseMatrix, false);

            float innerRadius = GetNormalizedInnerRadius(light);
            float innerAngle = GetNormalizedAngle(light.pointLightInnerAngle);
            float outerAngle = GetNormalizedAngle(light.pointLightOuterAngle);
            float innerRadiusMult = 1 / (1 - innerRadius);

            cmdBuffer.SetGlobalVector("_LightPosition", light.transform.position);
            cmdBuffer.SetGlobalMatrix("_LightInvMatrix", lightInverseMatrix);
            cmdBuffer.SetGlobalMatrix("_LightNoRotInvMatrix", lightNoRotInverseMatrix);
            cmdBuffer.SetGlobalFloat("_InnerRadiusMult", innerRadiusMult);

            cmdBuffer.SetGlobalFloat("_OuterAngle", outerAngle);
            cmdBuffer.SetGlobalFloat("_InnerAngleMult", 1 / (outerAngle - innerAngle));
            cmdBuffer.SetGlobalTexture("_LightLookup", GetLightLookupTexture());
            cmdBuffer.SetGlobalTexture("_FalloffLookup", GetFalloffLookupTexture());
            cmdBuffer.SetGlobalFloat("_FalloffIntensity", light.falloffIntensity);

            cmdBuffer.SetGlobalFloat("_LightZDistance", light.pointLightDistance);

            if (light.lightCookieSprite != null && light.lightCookieSprite.texture != null)
                cmdBuffer.SetGlobalTexture("_PointLightCookieTex", light.lightCookieSprite.texture);
        }

        static public void ClearDirtyLighting(CommandBuffer cmdBuffer)
        {
            for (int i = 0; i < s_BlendStyles.Length; ++i)
            {
                if (s_LightRenderTargetsDirty[i])
                {
                    cmdBuffer.SetRenderTarget(s_LightRenderTargets[i].Identifier());
                    cmdBuffer.ClearRenderTarget(false, true, Color.black);
                    s_LightRenderTargetsDirty[i] = false;
                }
            }
        }

        static public void RenderNormals(ScriptableRenderContext renderContext, CullingResults cullResults, DrawingSettings drawSettings, FilteringSettings filterSettings)
        {
            var cmd = CommandBufferPool.Get("Clear Normals");
            cmd.SetRenderTarget(s_NormalsTarget.Identifier());
            cmd.ClearRenderTarget(true, true, k_NormalClearColor);
            renderContext.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            drawSettings.SetShaderPassName(0, k_NormalsRenderingPassName);
            renderContext.DrawRenderers(cullResults, ref drawSettings, ref filterSettings);
        }

        static public void RenderLights(Camera camera, CommandBuffer cmdBuffer, int layerToRender)
        {
            for (int i = 0; i < s_BlendStyles.Length; ++i)
            {

                if (!s_BlendStyles[i].enabled)
                    continue;

                string sampleName = s_BlendStyles[i].name;
                cmdBuffer.BeginSample(sampleName);

                cmdBuffer.SetRenderTarget(s_LightRenderTargets[i].Identifier());

                bool rtDirty = false;
                Color clearColor;
                if (!Light2DManager.GetGlobalColor(layerToRender, i, out clearColor))
                    clearColor = Color.black;
                else
                    rtDirty = true;

                if (s_LightRenderTargetsDirty[i] || rtDirty)
                    cmdBuffer.ClearRenderTarget(false, true, clearColor);

                rtDirty |= RenderLightSet(
                    camera,
                    i,
                    cmdBuffer,
                    layerToRender,
                    Light2D.GetLightsByBlendStyle(i),
                    s_LightRenderTargets[i].Identifier()
                );

                s_LightRenderTargetsDirty[i] = rtDirty;

                cmdBuffer.EndSample(sampleName);
            }
        }

        static public void RenderLightVolumes(Camera camera, CommandBuffer cmdBuffer, int layerToRender)
        {
            for (int i = 0; i < s_BlendStyles.Length; ++i)
            {
                if (!s_BlendStyles[i].enabled)
                    continue;

                string sampleName = s_BlendStyles[i].name;
                cmdBuffer.BeginSample(sampleName);

                RenderLightVolumeSet(
                    camera,
                    i,
                    cmdBuffer,
                    layerToRender,
                    s_LightRenderTargets[i].Identifier(),
                    Light2D.GetLightsByBlendStyle(i)                  
                );

                cmdBuffer.EndSample(sampleName);
            }
        }

        static void SetBlendModes(Material material, BlendMode src, BlendMode dst)
        {
            material.SetFloat("_SrcBlend", (float)src);
            material.SetFloat("_DstBlend", (float)dst);
        }

        static uint GetLightMaterialIndex(Light2D light, bool isVolume)
        {
            int bitIndex = 0;
            uint volumeBit = isVolume ? 1u << bitIndex : 0u;
            bitIndex++;
            uint shapeBit = light.IsShapeLight() ? 1u << bitIndex : 0u;
            bitIndex++;
            uint additiveBit = light.alphaBlendOnOverlap ? 0u : 1u << bitIndex;
            bitIndex++;
            uint spriteBit = light.lightType == Light2D.LightType.Sprite ? 1u << bitIndex : 0u;
            bitIndex++;
            uint pointCookieBit = (!light.IsShapeLight() && light.lightCookieSprite != null && light.lightCookieSprite.texture != null) ? 1u << bitIndex : 0u;
            bitIndex++;
            uint pointFastQualityBit = (!light.IsShapeLight() && light.pointLightQuality == Light2D.PointLightQuality.Fast) ? 1u << bitIndex : 0u;
            bitIndex++;
            uint useNormalMap = light.useNormalMap ? 1u << bitIndex : 0u;

            return pointFastQualityBit | pointCookieBit | spriteBit | additiveBit | shapeBit | volumeBit | useNormalMap;
        }

        static Material CreateLightMaterial(Light2D light, bool isVolume)
        {
            bool isShape = light.IsShapeLight();
            Material material;

            if (isVolume)
                material = CoreUtils.CreateEngineMaterial(isShape ? s_RendererData.shapeLightVolumeShader : s_RendererData.pointLightVolumeShader);
            else
            {
                material = CoreUtils.CreateEngineMaterial(isShape ? s_RendererData.shapeLightShader : s_RendererData.pointLightShader);

                if (!light.alphaBlendOnOverlap)
                {
                    SetBlendModes(material, BlendMode.One, BlendMode.One);
                    material.EnableKeyword(k_UseAdditiveBlendingKeyword);
                }
                else
                    SetBlendModes(material, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
            }

            if (light.lightType == Light2D.LightType.Sprite)
                material.EnableKeyword(k_SpriteLightKeyword);

            if (!isShape && light.lightCookieSprite != null && light.lightCookieSprite.texture != null)
                material.EnableKeyword(k_UsePointLightCookiesKeyword);

            if (!isShape && light.pointLightQuality == Light2D.PointLightQuality.Fast)
                material.EnableKeyword(k_LightQualityFastKeyword);

            if (light.useNormalMap)
                material.EnableKeyword(k_UseNormalMap);

            return material;
        }

        static Material GetLightMaterial(Light2D light, bool isVolume)
        {
            uint materialIndex = GetLightMaterialIndex(light, isVolume);

            if (s_LightMaterials[materialIndex] == null)
                s_LightMaterials[materialIndex] = CreateLightMaterial(light, isVolume);

            return s_LightMaterials[materialIndex];
        }

        static Material GetShadowMaterial(int index)
        {
            const int totalMaterials = 255;

            if (s_ShadowMaterials == null)
            {
                s_ShadowMaterials = new Material[totalMaterials];

                Material material = CoreUtils.CreateEngineMaterial(s_RendererData.shadowShader);
                for (int i = 0; i < totalMaterials; i++)
                {
                    s_ShadowMaterials[i] = new Material(material);
                    s_ShadowMaterials[i].SetFloat("_ShadowStencilGroup", i+1);
                }
            }

            return s_ShadowMaterials[index%totalMaterials];
        }

        static Material GetRemoveSelfShadowMaterial()
        {
            if(s_RemoveSelfShadowMaterial == null)
                s_RemoveSelfShadowMaterial = CoreUtils.CreateEngineMaterial(s_RendererData.removeSelfShadowShader);

            return s_RemoveSelfShadowMaterial;
        }
    }
}
