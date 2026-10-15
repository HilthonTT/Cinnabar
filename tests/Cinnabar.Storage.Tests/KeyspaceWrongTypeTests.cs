namespace Cinnabar.Storage.Tests;

public class KeyspaceWrongTypeTests : KeyspaceTestBase
{
    private const string WrongTypeMessage = "WRONGTYPE Operation against a key holding the wrong kind of value";

    public KeyspaceWrongTypeTests()
    {
        Keyspace.SetString("string", "v");
        Keyspace.ListPush("list", ["a", "b"], ListEnd.Right);
        Keyspace.StreamAdd("stream", "1-1", Fields("f", "v"));
    }

    private void Run(string operation, string key)
    {
        switch (operation)
        {
            case "GetString": Keyspace.GetString(key); break;
            case "IncrementBy": Keyspace.IncrementBy(key, 1); break;
            case "ListPush": Keyspace.ListPush(key, ["x"], ListEnd.Left); break;
            case "ListPop": Keyspace.ListPop(key, ListEnd.Left); break;
            case "ListLength": Keyspace.ListLength(key); break;
            case "ListRange": Keyspace.ListRange(key, 0, -1); break;
            case "StreamAdd": Keyspace.StreamAdd(key, "*", Fields("f", "v")); break;
            case "StreamRange": Keyspace.StreamRange(key, StreamId.Min, StreamId.Max); break;
            case "StreamReadAfter": Keyspace.StreamReadAfter(key, StreamId.Min); break;
            case "StreamLastId": Keyspace.StreamLastId(key); break;
            case "StreamLength": Keyspace.StreamLength(key); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    [Theory]
    [InlineData("GetString", "list")]
    [InlineData("GetString", "stream")]
    [InlineData("IncrementBy", "list")]
    [InlineData("IncrementBy", "stream")]
    [InlineData("ListPush", "string")]
    [InlineData("ListPush", "stream")]
    [InlineData("ListPop", "string")]
    [InlineData("ListPop", "stream")]
    [InlineData("ListLength", "string")]
    [InlineData("ListLength", "stream")]
    [InlineData("ListRange", "string")]
    [InlineData("ListRange", "stream")]
    [InlineData("StreamAdd", "string")]
    [InlineData("StreamAdd", "list")]
    [InlineData("StreamRange", "string")]
    [InlineData("StreamRange", "list")]
    [InlineData("StreamReadAfter", "string")]
    [InlineData("StreamReadAfter", "list")]
    [InlineData("StreamLastId", "string")]
    [InlineData("StreamLastId", "list")]
    [InlineData("StreamLength", "string")]
    [InlineData("StreamLength", "list")]
    public void Typed_operation_on_wrong_type_throws_wrongtype(string operation, string key)
    {
        Action act = () => Run(operation, key);

        act.Should().Throw<WrongTypeException>().WithMessage(WrongTypeMessage);
    }

    [Theory]
    [InlineData("ListPush", "string")]
    [InlineData("StreamAdd", "list")]
    [InlineData("IncrementBy", "stream")]
    public void Wrong_type_error_leaves_value_untouched(string operation, string key)
    {
        KeyType before = Keyspace.GetKeyType(key);

        try
        {
            Run(operation, key);
        }
        catch (WrongTypeException)
        {
        }

        Keyspace.GetKeyType(key).Should().Be(before);
        Keyspace.GetString("string").Should().Be("v");
        Keyspace.ListRange("list", 0, -1).Should().Equal("a", "b");
        Keyspace.StreamLength("stream").Should().Be(1);
    }

    [Fact]
    public void WrongTypeException_is_a_StorageException()
    {
        new WrongTypeException().Should().BeAssignableTo<StorageException>();
    }

    [Theory]
    [InlineData("string")]
    [InlineData("list")]
    [InlineData("stream")]
    public void Generic_operations_work_on_any_type(string key)
    {
        Keyspace.Exists(key).Should().BeTrue();
        Keyspace.Expire(key, TimeSpan.FromSeconds(5)).Should().BeTrue();
        Keyspace.GetTimeToLiveMilliseconds(key).Should().Be(5_000);
        Keyspace.Persist(key).Should().BeTrue();
        Keyspace.Delete(key).Should().BeTrue();
        Keyspace.Exists(key).Should().BeFalse();
    }
}
