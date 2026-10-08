using System.Linq;
using Pecera.Core;
using Xunit;

namespace Pecera.Tests
{
    public class NombreMiembroTests
    {
        [Theory]
        [InlineData("id", true)] [InlineData("ID", true)] [InlineData("Id", true)] [InlineData("guid", true)] [InlineData("Guid", true)]
        [InlineData("uniqueId", true)] [InlineData("UniqueID", true)] [InlineData("pawnId", true)] [InlineData("pawn_id", true)]
        [InlineData("<id>k__BackingField", true)] [InlineData("uid", true)] [InlineData("characterUUID", true)] [InlineData("saveGuid", true)]
        [InlineData("valid", false)] [InlineData("width", false)] [InlineData("identity", false)] [InlineData("provider", false)]
        [InlineData("hidden", false)] [InlineData("idle", false)] [InlineData("keyCode", false)] [InlineData("grid", false)] [InlineData("", false)]
        public void Reconoce_identificadores_por_tokens_y_no_por_subcadena(string nombre, bool esperado)
        {
            Assert.Equal(esperado, NombreMiembro.PareceId(nombre));
        }

        [Fact]
        public void Tokens_camelCase()
        {
            Assert.Equal(new[] { "pawn", "id" }, NombreMiembro.TokensDe("pawnId").ToArray());
            Assert.Equal(new[] { "http", "request", "id" }, NombreMiembro.TokensDe("HTTPRequestId").ToArray());
            Assert.Equal(new[] { "id" }, NombreMiembro.TokensDe("<id>k__BackingField".Replace("k__BackingField", "")).ToArray());
            Assert.Empty(NombreMiembro.TokensDe(null));
        }
    }
}
