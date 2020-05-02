using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace uSync.UserEdition.Rdbms
{
    [TableName("cmsContent")]
    [PrimaryKey("pk")]
    [ExplicitColumns]
    internal class ContentDto
    {
        [Column("pk")]
        [PrimaryKeyColumn]
        public int PrimaryKey { get; set; }

    }
}
