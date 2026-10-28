using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// OGG（Vorbis）のファイルのタグ（コメント）を読む。ループの位置は LOOPSTART と LOOPLENGTH（サンプル数。ファイルの
    /// サンプリング周波数の単位）で書く（RPG ツクールなどと同じ書き方。docs/18 6 章）。Unity の AudioClip はタグを読まないので、
    /// エディタのビルダーがこれで読み、MusicCue に入れる。
    /// </summary>
    public static class OggLoopTags
    {
        /// <summary>ファイルのタグ（名前は大文字に。同じ名前が複数あれば最後のもの）。</summary>
        public static Dictionary<string, string> Read(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                return Read(stream);
            }
        }

        public static Dictionary<string, string> Read(Stream stream)
        {
            byte[] packet = ReadPacket(stream, 1);  // 0 番目は識別、1 番目がコメント
            const string Header = "\u0003vorbis";
            if (packet.Length < 7 || Encoding.ASCII.GetString(packet, 0, 7) != Header)
            {
                throw new InvalidDataException("Vorbis のコメントのヘッダーがない");
            }
            int position = 7;
            int vendorLength = ReadInt(packet, ref position);
            position += vendorLength;
            int count = ReadInt(packet, ref position);
            var tags = new Dictionary<string, string>();
            for (int i = 0; i < count; i++)
            {
                int length = ReadInt(packet, ref position);
                string comment = Encoding.UTF8.GetString(packet, position, length);
                position += length;
                int equals = comment.IndexOf('=');
                if (equals > 0)
                {
                    tags[comment.Substring(0, equals).ToUpperInvariant()] = comment.Substring(equals + 1);
                }
            }
            return tags;
        }

        /// <summary>ループの始まりと終わり（サンプル）。タグがなければ false。終わりは始まり＋長さ（LOOPLENGTH）か LOOPEND。</summary>
        public static bool TryGetLoop(Dictionary<string, string> tags, out long start, out long end)
        {
            start = 0;
            end = 0;
            if (!tags.TryGetValue("LOOPSTART", out string startText) || !long.TryParse(startText, out start))
            {
                return false;
            }
            if (tags.TryGetValue("LOOPLENGTH", out string lengthText) && long.TryParse(lengthText, out long length))
            {
                end = start + length;
                return true;
            }
            return tags.TryGetValue("LOOPEND", out string endText) && long.TryParse(endText, out end);
        }

        private static int ReadInt(byte[] data, ref int position)
        {
            if (position + 4 > data.Length)
            {
                throw new InvalidDataException("Vorbis のコメントが途中で切れている");
            }
            int value = BitConverter.ToInt32(data, position);
            position += 4;
            return value;
        }

        // Ogg のページを読み、index 番目のパケットを組み立てる（パケットはページをまたぐことがある）。
        private static byte[] ReadPacket(Stream stream, int index)
        {
            var current = new List<byte>();
            int packet = 0;
            var header = new byte[27];
            while (true)
            {
                if (ReadFully(stream, header, 27) < 27)
                {
                    throw new InvalidDataException("Ogg のパケットが足りない");
                }
                if (header[0] != 'O' || header[1] != 'g' || header[2] != 'g' || header[3] != 'S')
                {
                    // Git LFS の目印のファイル（"version https://git-lfs..."）なら、そう知らせる（曲のファイルを取り込んでいない）。
                    if (Encoding.ASCII.GetString(header, 0, 8) == "version ")
                    {
                        throw new InvalidDataException("曲のファイルではなく Git LFS の目印のファイル（手もとで git lfs pull をしていない）");
                    }
                    throw new InvalidDataException("Ogg のページの印がない");
                }
                int segments = header[26];
                var table = new byte[segments];
                ReadFully(stream, table, segments);
                foreach (byte lace in table)
                {
                    var data = new byte[lace];
                    ReadFully(stream, data, lace);
                    if (packet == index)
                    {
                        current.AddRange(data);
                    }
                    if (lace < 255)
                    {
                        // パケットの終わり。
                        if (packet == index)
                        {
                            return current.ToArray();
                        }
                        packet++;
                    }
                }
            }
        }

        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0)
                {
                    break;
                }
                read += n;
            }
            return read;
        }
    }
}
