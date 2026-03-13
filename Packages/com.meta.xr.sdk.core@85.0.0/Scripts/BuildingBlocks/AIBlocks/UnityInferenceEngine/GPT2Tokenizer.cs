/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using UnityEngine;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// GPT-2 style BPE tokenizer for text-only LLMs (SmolLM, Qwen, Phi, etc.)
    /// Supports byte-level encoding and special tokens for chat templates.
    /// Provides both synchronous and asynchronous (background thread) tokenization modes.
    /// </summary>
    public class Gpt2Tokenizer
    {
        private TextAsset _vocabFile;
        private TextAsset _mergesFile;
        private TextAsset _tokenizerConfigFile;

        private Dictionary<string, int> _encoder;
        private Dictionary<int, string> _decoder;
        private Dictionary<byte, char> _byteEncoder;
        private Dictionary<char, byte> _byteDecoder;
        private Dictionary<(string, string), int> _bpeRanks;
        private readonly Dictionary<string, List<string>> _bpeCache = new();
        private readonly HashSet<(string, string)> _reusablePairsBuffer = new();

        private HashSet<string> _specialTokens;
        private Regex _specialTokensRegex;
        private Regex _preTokenizeRegex;

        private int EosTokenId { get; set; }
        public int PadTokenId { get; private set; }
        private int UnkTokenId { get; set; }

        private bool _isInitialized;

        private const string PreTokenizePattern =
            @"(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+";

        public void Initialize(TextAsset vocab, TextAsset merges, TextAsset config)
        {
            if (_isInitialized)
            {
                return;
            }

            _vocabFile = vocab;
            _mergesFile = merges;
            _tokenizerConfigFile = config;

            if (!_vocabFile || !_mergesFile || !_tokenizerConfigFile)
            {
                Debug.LogError("[GPT2Tokenizer] All 3 tokenizer files (vocab, merges, config) must be assigned.");
                return;
            }

            _encoder = ParseVocabJson(_vocabFile.text);
            var tokenizerConfig = ParseTokenizerConfig(_tokenizerConfigFile.text);

            if (tokenizerConfig?.AddedTokensDecoder != null)
            {
                foreach (var keyValuePair in tokenizerConfig.AddedTokensDecoder)
                {
                    if (!int.TryParse(keyValuePair.Key, out var tokenId))
                    {
                        continue;
                    }

                    var tokenContent = keyValuePair.Value.Content;
                    _encoder[tokenContent] = tokenId;
                }
            }

            _decoder = _encoder.ToDictionary(keyValuePair => keyValuePair.Value, keyValuePair => keyValuePair.Key);

            _specialTokens = new HashSet<string>();
            if (tokenizerConfig?.AddedTokensDecoder != null)
            {
                foreach (var tokenDef in tokenizerConfig.AddedTokensDecoder.Values)
                {
                    if (tokenDef.Special)
                    {
                        _specialTokens.Add(tokenDef.Content);
                    }
                }
            }

            if (_specialTokens.Any())
            {
                var escapedTokens = _specialTokens.Select(Regex.Escape);
                _specialTokensRegex = new Regex($"({string.Join("|", escapedTokens)})", RegexOptions.Compiled);
            }
            else
            {
                _specialTokensRegex = new Regex("(?!)", RegexOptions.Compiled);
            }

            if (tokenizerConfig is { EosToken: not null } &&
                _encoder.TryGetValue(tokenizerConfig.EosToken, out var eosId))
            {
                EosTokenId = eosId;
            }
            else
            {
                Debug.LogWarning(
                    "[GPT2Tokenizer] eos_token not found in tokenizer config or vocab. Using default value.");
                EosTokenId = 0;
            }

            if (tokenizerConfig is { PadToken: not null } &&
                _encoder.TryGetValue(tokenizerConfig.PadToken, out var padId))
            {
                PadTokenId = padId;
            }
            else
            {
                Debug.LogWarning(
                    "[GPT2Tokenizer] pad_token not found in tokenizer config or vocab. Using EOS token as fallback.");
                PadTokenId = EosTokenId;
            }

            if (tokenizerConfig is { UnkToken: not null } &&
                _encoder.TryGetValue(tokenizerConfig.UnkToken, out var unkId))
            {
                UnkTokenId = unkId;
            }
            else
            {
                Debug.LogWarning(
                    "[GPT2Tokenizer] unk_token not found or is null in tokenizer config. Using 0 as fallback.");
                UnkTokenId = 0;
            }

            _bpeRanks = LoadMergesFromString(_mergesFile.text);

            (_byteEncoder, _byteDecoder) = BuildByteToUnicodeMap();
            _preTokenizeRegex = new Regex(PreTokenizePattern, RegexOptions.Compiled);

            _isInitialized = true;
        }

        private List<int> Encode(string text)
        {
            if (!_isInitialized)
            {
                Debug.LogError("[GPT2Tokenizer] Not initialized.");
                return new List<int>();
            }

            text = text.Normalize(NormalizationForm.FormC);
            var tokenIds = new List<int>();

            var parts = _specialTokensRegex.Split(text);

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                if (_specialTokens.Contains(part))
                {
                    tokenIds.Add(_encoder[part]);
                }
                else
                {
                    var matches = _preTokenizeRegex.Matches(part);
                    foreach (Match match in matches)
                    {
                        var builder = new StringBuilder();
                        foreach (var b in Encoding.UTF8.GetBytes(match.Value))
                        {
                            builder.Append(_byteEncoder[b]);
                        }

                        var bpeTokens = Bpe(builder.ToString());
                        foreach (var token in bpeTokens)
                        {
                            if (_encoder.TryGetValue(token, out var id))
                            {
                                tokenIds.Add(id);
                            }
                            else
                            {
                                Debug.LogWarning($"[GPT2Tokenizer] '{token}' not found in vocab. Using UNK token.");
                                tokenIds.Add(UnkTokenId);
                            }
                        }
                    }
                }
            }

            return tokenIds;
        }

        /// <summary>
        /// Asynchronously encodes text to token IDs on a background thread.
        /// Use this to avoid blocking the main thread and prevent frame drops.
        /// May be slower overall, but maintains consistent frame rate.
        /// </summary>
        public async Task<List<int>> EncodeAsync(string text, CancellationToken ct = default)
        {
            if (_isInitialized)
            {
                return await Task.Run(() => ct.IsCancellationRequested ? new List<int>() : Encode(text), ct);
            }

            Debug.LogError("[GPT2Tokenizer] Not initialized.");
            return new List<int>();
        }

        public string Decode(List<int> tokenIds)
        {
            if (!_isInitialized)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var id in tokenIds)
            {
                if (!_decoder.TryGetValue(id, out var token))
                {
                    continue;
                }

                if (_specialTokens.Contains(token))
                {
                    builder.Append(token);
                }
                else
                {
                    var byteBuffer = new List<byte>();
                    foreach (var c in token)
                    {
                        if (_byteDecoder.TryGetValue(c, out var b))
                        {
                            byteBuffer.Add(b);
                        }
                    }

                    builder.Append(Encoding.UTF8.GetString(byteBuffer.ToArray()));
                }
            }

            return builder.ToString();
        }

        private List<string> Bpe(string token)
        {
            if (_bpeCache.TryGetValue(token, out var cachedResult))
            {
                return cachedResult;
            }

            if (token.Length <= 1)
            {
                var result = new List<string> { token };
                _bpeCache[token] = result;
                return result;
            }

            var word = token.Select(c => c.ToString()).ToList();

            while (word.Count > 1)
            {
                var pairs = GetPairs(word);
                var bestPair = pairs.OrderBy(p => _bpeRanks.GetValueOrDefault(p, int.MaxValue)).First();
                if (!_bpeRanks.ContainsKey(bestPair)) break;

                var newWord = new List<string>();
                var i = 0;
                while (i < word.Count)
                {
                    if (i < word.Count - 1 && word[i] == bestPair.Item1 && word[i + 1] == bestPair.Item2)
                    {
                        newWord.Add(bestPair.Item1 + bestPair.Item2);
                        i += 2;
                    }
                    else
                    {
                        newWord.Add(word[i]);
                        i++;
                    }
                }

                word = newWord;
            }

            _bpeCache[token] = word;
            return word;
        }

        private static HashSet<(string, string)> GetPairs(List<string> word)
        {
            var pairs = new HashSet<(string, string)>();
            if (word.Count < 2) return pairs;
            for (var i = 0; i < word.Count - 1; i++)
            {
                pairs.Add((word[i], word[i + 1]));
            }

            return pairs;
        }

        private static Dictionary<(string, string), int> LoadMergesFromString(string mergesContent)
        {
            var ranks = new Dictionary<(string, string), int>();
            var lines = mergesContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var rank = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split(' ');
                if (parts.Length == 2) ranks[(parts[0], parts[1])] = rank++;
            }

            return ranks;
        }

        private static (Dictionary<byte, char>, Dictionary<char, byte>) BuildByteToUnicodeMap()
        {
            var byteToUnicode = new Dictionary<byte, char>();
            var unicodeToByte = new Dictionary<char, byte>();

            var visibleChars = Enumerable.Range('!', '~' - '!' + 1)
                .Concat(Enumerable.Range('¡', '¬' - '¡' + 1))
                .Concat(Enumerable.Range('®', 'ÿ' - '®' + 1))
                .Select(i => (byte)i).ToHashSet();

            var n = 0;
            for (var b = 0; b < 256; b++)
            {
                char mappedChar;
                if (visibleChars.Contains((byte)b))
                {
                    mappedChar = (char)b;
                }
                else
                {
                    mappedChar = (char)(256 + n);
                    n++;
                }

                byteToUnicode[(byte)b] = mappedChar;
                unicodeToByte[mappedChar] = (byte)b;
            }

            return (byteToUnicode, unicodeToByte);
        }

        private static Dictionary<string, int> ParseVocabJson(string json)
        {
            var vocab = new Dictionary<string, int>();
            var idx = 1;

            while (idx < json.Length - 1)
            {
                idx = json.IndexOf('"', idx);
                if (idx == -1) break;

                var keyStart = idx + 1;
                var keyEnd = keyStart;

                while (keyEnd < json.Length)
                {
                    if (json[keyEnd] == '\\')
                    {
                        keyEnd += 2;
                        continue;
                    }
                    if (json[keyEnd] == '"') break;
                    keyEnd++;
                }

                if (keyEnd >= json.Length) break;

                var key = json.Substring(keyStart, keyEnd - keyStart);
                key = key.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\/", "/");

                idx = json.IndexOf(':', keyEnd);
                if (idx == -1) break;
                idx++;

                while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;

                var numStart = idx;
                while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '-')) idx++;

                if (idx <= numStart) continue;
                var numStr = json.Substring(numStart, idx - numStart);
                if (int.TryParse(numStr, out var value))
                {
                    vocab[key] = value;
                }
            }

            return vocab;
        }

        private static TokenizerConfig ParseTokenizerConfig(string json)
        {
            var config = new TokenizerConfig();

            var eosMatch = Regex.Match(json, @"""eos_token""\s*:\s*""([^""]+)""");
            if (eosMatch.Success) config.EosToken = eosMatch.Groups[1].Value;

            var padMatch = Regex.Match(json, @"""pad_token""\s*:\s*""([^""]+)""");
            if (padMatch.Success) config.PadToken = padMatch.Groups[1].Value;

            var unkMatch = Regex.Match(json, @"""unk_token""\s*:\s*""([^""]+)""");
            if (unkMatch.Success) config.UnkToken = unkMatch.Groups[1].Value;

            config.AddedTokensDecoder = new Dictionary<string, AddedTokenDef>();
            var tokenIdPattern = @"""(\d+)""\s*:\s*\{";
            var contentPattern = @"""content""\s*:\s*""([^""]+)""";
            var specialPattern = @"""special""\s*:\s*(\w+)";

            foreach (Match idMatch in Regex.Matches(json, tokenIdPattern))
            {
                var id = idMatch.Groups[1].Value;
                var startIdx = idMatch.Index;
                var braceCount = 1;
                var idx = idMatch.Index + idMatch.Length;

                while (idx < json.Length && braceCount > 0)
                {
                    switch (json[idx])
                    {
                        case '{':
                            braceCount++;
                            break;
                        case '}':
                            braceCount--;
                            break;
                    }

                    idx++;
                }

                var tokenBlock = json.Substring(startIdx, idx - startIdx);

                var contentMatch = Regex.Match(tokenBlock, contentPattern);
                var specialMatch = Regex.Match(tokenBlock, specialPattern);

                if (!contentMatch.Success) continue;
                var content = contentMatch.Groups[1].Value;
                var special = specialMatch.Success && specialMatch.Groups[1].Value == "true";
                config.AddedTokensDecoder[id] = new AddedTokenDef { Content = content, Special = special };
            }

            return config;
        }

        private class TokenizerConfig
        {
            public string EosToken { get; set; }
            public string PadToken { get; set; }
            public string UnkToken { get; set; }
            public Dictionary<string, AddedTokenDef> AddedTokensDecoder { get; set; }
        }

        private class AddedTokenDef
        {
            public string Content { get; set; }
            public bool Special { get; set; }
        }
    }
}
