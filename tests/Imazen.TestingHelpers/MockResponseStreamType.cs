namespace Imazen.Routing.Tests.Serving;

[Flags]
public enum MockResponseStreamType
{
    None,
    Stream,
    Pipe,
    BufferWriter
}