using Xunit;

namespace WindowsMcpNet.ParityTests.Infrastructure;

[CollectionDefinition("McpServer")]
public class McpServerCollection : ICollectionFixture<McpServerFixture>
{
    // This class has no code; it's just used to define the collection.
}
