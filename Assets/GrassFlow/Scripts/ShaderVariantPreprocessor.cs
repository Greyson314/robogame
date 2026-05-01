using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

namespace SVPreprocessor {

    public class ShaderVariantPreprocessor {


        public static string resourceFolder = "";

        const char preMarker = '#';
        const string ifKey = "if";
        const string endifKey = "endif";
        const string incKey = "svpinclude";
        const string nameKey = "[NAME]";

        public static string CompileShader(string templateFilename, string shaderName, params string[] keywords) {
            return CompileShader(templateFilename, shaderName, keywords);
        }

        public static string CompileShader(string templateFilename, string shaderName, IEnumerable<string> keywords) {

            HashSet<string> keys = new HashSet<string>(keywords);

            var result = PreParse(templateFilename, keys);
            result.Replace("\n\n\n", "\n").Replace("\n\n", "\n");

            string resultName = shaderName;
            result.Replace(nameKey, resultName);

            if (keys.Contains("SRP")) {
                result.Replace("CGPROGRAM", "HLSLPROGRAM");
                result.Replace("ENDCG", "ENDHLSL");
            }

            //Debug.Log(result.ToString());
            return result.ToString();
        }


        static StringBuilder PreParse(string fileName, HashSet<string> keywords) {

            TextAsset templateData = Resources.Load<TextAsset>(resourceFolder + fileName);
            string templateText = templateData.text.Trim();
            StringBuilder tmp = new StringBuilder(templateText);

            Action<int> HandleInclude = (int pos) => {
                int keywordOffset = pos + incKey.Length + 3;
                int nameEnd = tmp.IndexOf(keywordOffset, '"');

                string includeName = tmp.ToString(keywordOffset, nameEnd - keywordOffset);
                //Debug.Log(includeName);
                StringBuilder include = PreParse(includeName, keywords);

                tmp.Remove(pos, (nameEnd - pos) + 1);
                tmp.Insert(pos, include.ToString());
            };

            Func<int, string> GetKey = (int i) => {
                int keyedge = tmp.FindEdge(i);
                return tmp.ToString(i + 1, keyedge - (i + 1)).Trim().ToLower();
            };

            Action<int> HandleIf = (int pos) => {
                int keyStart = pos + ifKey.Length + 2;
                int keyEnd = tmp.FindEdge(keyStart);
                string keyword = tmp.ToString(keyStart, keyEnd - keyStart).Trim();

                bool notKeyword = false;

                if (keyword[0] == '!') {
                    keyword = keyword.Substring(1);
                    notKeyword = true;
                }

                //Debug.Log(keyword);

                int endifOffset = endifKey.Length + 1;
                int endPoint = -1;
                int nestLevel = 1;
                for (int i = keyEnd; i < keyEnd + 999999; i++) {
                    if (tmp[i] == preMarker) {
                        string key = GetKey(i);
                        if (key == ifKey) {
                            nestLevel++;
                        }
                        else if (key == endifKey) {
                            nestLevel--;
                            if (nestLevel <= 0) {
                                endPoint = i + endifKey.Length + 1;
                                break;
                            }
                        }
                    }
                }


                if (keywords.Contains(keyword) != notKeyword) {
                    tmp.Remove(endPoint - endifOffset, endifOffset);
                    tmp.Remove(pos, keyEnd - pos);
                }
                else {
                    tmp.Remove(pos, endPoint - pos);
                }
            };


            for (int i = 0; i < tmp.Length; i++) {
                char c = tmp[i];

                if (c == preMarker) {
                    string key = GetKey(i);
                    //Debug.Log(i + ": " + key);
                    switch (key) {
                        case incKey:
                            HandleInclude(i);
                            break;

                        case ifKey:
                            HandleIf(i);
                            break;
                    }
                }
            }

            return tmp;
        }
    }

    static class StringExt {

        public static int IndexOf(this StringBuilder tmp, int startIdx, char c) {
            for (int i = startIdx; i < startIdx + 999; i++) {
                if (tmp[i] == c) {
                    return i;
                }
            }

            return -1;
        }

        public static int FindEdge(this StringBuilder tmp, int startIdx) {
            for (int i = startIdx; i < startIdx + 999; i++) {
                if (i >= tmp.Length || char.IsWhiteSpace(tmp[i])) {
                    return i;
                }
            }

            return -1;
        }
    }

}