using System.Text;

public readonly unsafe struct Utf8Span(byte* data, int length)
{
    public override string ToString()
        => Encoding.UTF8.GetString(data, length);
}
