using Gardener.Core;
using System.Text;

namespace UpgradeRepo
{
    internal class ProjFileInfo
    {
        private readonly object _lockObject = new();
        private readonly IFileSystem _fileSystem;
        private Encoding? _encoding;
        private bool _endsWithNewLine;

        public ProjFileInfo(IFileSystem fileSystem, string path)
        {
            _fileSystem = fileSystem;
            FullName = path;
        }

        public string FullName { get; set; }

        public Encoding Encoding
        {
            get
            {
                EnsureReadFile();
                return _encoding!;
            }
        }

        public bool EndsWithNewLine
        {
            get
            {
                EnsureReadFile();
                return _endsWithNewLine;
            }
        }

        private void EnsureReadFile()
        {
            if (_encoding != null)
            {
                return;
            }

            lock (_lockObject)
            {
                if (_encoding == null)
                {
                    var (encoding, endsWithNewLine) = ReadFileAsync(FullName).Result;
                    _encoding = encoding;
                    _endsWithNewLine = endsWithNewLine;
                }
            }
        }

        private async Task<(Encoding Encoding, bool EndsWithNewLine)> ReadFileAsync(string path)
        {
            byte[] bytes = await _fileSystem.ReadAllBytesAsync(path);
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
                    encUtf8Bom.GetString(bytes, prLen, bytes.Length - prLen);
                    encoding = encUtf8Bom;
                }
                catch (ArgumentException)
                {
                    // Confirmed as not UTF-8!
                    couldBeUtf8 = false;
                }
            }

            // use boolean to skip this if it's already confirmed as incorrect UTF-8 decoding.
            if (couldBeUtf8 && encoding == null)
            {
                // test UTF-8 on strict encoding rules. Note that on pure ASCII this will
                // succeed as well, since valid ASCII is automatically valid UTF-8.
                UTF8Encoding encUtf8NoBom = new UTF8Encoding(false, true);
                try
                {
                    encUtf8NoBom.GetString(bytes);
                    encoding = encUtf8NoBom;
                }
                catch (ArgumentException)
                {
                    // Confirmed as not UTF-8!
                }
            }

            // fall back to default ANSI encoding.
            if (encoding == null)
            {
                encoding = Encoding.GetEncoding(1252);
                encoding.GetString(bytes);
            }

            bool endsWithNewLine = bytes.AsSpan().EndsWith(encoding.GetBytes(Environment.NewLine));

            return (encoding, endsWithNewLine);
        }
    }
}
