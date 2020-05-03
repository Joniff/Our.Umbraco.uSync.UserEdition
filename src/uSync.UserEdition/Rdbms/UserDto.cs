using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace uSync.UserEdition.Rdbms
{
    [TableName("umbracoUser")]
    [PrimaryKey("id", autoIncrement = false)]
    [ExplicitColumns]
    internal class UserDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = false)]
        [ForeignKey(typeof(ContentDto), Column = "id")]
        [ForeignKey(typeof(NodeDto))]
        public int Id { get; set; }

        [Column("userEmail")]
        [Length(1000)]
        [Constraint(Default = "''")]
        public string Email { get; set; }

        [Column("userLogin")]
        [Length(1000)]
        [Constraint(Default = "''")]
        public string LoginName { get; set; }

        [Column("userPassword")]
        [Length(1000)]
        [Constraint(Default = "''")]
        public string Password { get; set; }
    }
}