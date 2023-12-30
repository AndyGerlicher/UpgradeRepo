using Gardener.Core;
using Microsoft.VisualBasic;
using System.Text;

namespace UpgradeRepo
{
    internal class ProjFileInfo
    {
        private readonly IFileSystem _fileSystem;

        public string FullName { get; set; }

        public bool EndsWithNewLine { get; private set; }

        public string LineEndings { get; private set; }

        public ProjFileInfo(IFileSystem fileSystem, string path)
        {
            _fileSystem = fileSystem;
            FullName = path;

            var contents = ReadFileAsync(FullName).Result;
            LineEndings = contents.DetermineLineEnding();
        }

        private async Task<string> ReadFileAsync(string path)
        {
            byte[] bytes = await _fileSystem.ReadAllBytesAsync(path);
            string contents = null!;
            Encoding? encoding = null;

            // Test UTF8 with BOM. This check can easily be copied and adapted
            // to detect many other encodings that use BOMs.
            UTF8Encoding encUtf8Bom = new UTF8Encoding(true, true);
            bool couldBeUtf8 = true;
            byte[] preamble = encUtf8Bom.GetPreamble();
            int prLen = preamble.Length;
            if (bytes.AsSpan().StartsWith(preamble))
            {
                // UTF8 BOM found; use encUtf8Bom to decode.
                try
                {
                    // Seems that despite being an encoding with preamble,
                    // it doesn't actually skip said preamble when decoding...
                    contents = encUtf8Bom.GetString(bytes, prLen, bytes.Length - prLen);
                    encoding = encUtf8Bom;
                }
                catch (ArgumentException)
                {
                    // Confirmed as not UTF-8!
                }
            }
            else if (couldBeUtf8 && encoding == null)
            {
                // test UTF-8 on strict encoding rules. Note that on pure ASCII this will
                // succeed as well, since valid ASCII is automatically valid UTF-8.
                UTF8Encoding encUtf8NoBom = new UTF8Encoding(false, true);
                try
                {
                    contents = encUtf8NoBom.GetString(bytes);
                    encoding = encUtf8NoBom;
                }
                catch (ArgumentException)
                {
                    // Confirmed as not UTF-8!
                }
            }
            else
            {
                // fall back to default ANSI encoding.
                encoding = Encoding.GetEncoding(1252);
                contents = encoding.GetString(bytes);
                LineEndings = contents.DetermineLineEnding();
            }

            EndsWithNewLine = bytes.AsSpan().EndsWith(encoding!.GetBytes("\r")) ||
                              bytes.AsSpan().EndsWith(encoding!.GetBytes("\r\n"));

            return contents;
        }
    }
}
