using Dapper;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MySqlConnector;
namespace MarketSignalAI.Infrastructure;

public sealed class MySqlMarketReferenceStore(string connectionString) : IMarketReferenceStore
{
    private MySqlConnection Connection()=>new(connectionString);
    private static CommandDefinition Cmd(string sql,object? args,CancellationToken ct)=>new(sql,args,cancellationToken:ct);
    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db=Connection();
        using var stream=typeof(MySqlMarketReferenceStore).Assembly.GetManifestResourceStream("MarketSignalAI.Infrastructure.Migrations.004_market_references.sql") ?? throw new InvalidOperationException("Reference migration missing.");
        using var reader=new StreamReader(stream);
        foreach(var statement in (await reader.ReadToEndAsync(ct)).Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)) await db.ExecuteAsync(Cmd(statement,null,ct));
    }
    public async Task SeedAsync(CancellationToken ct)
    {
        await using var db=Connection();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var productId=await db.QuerySingleOrDefaultAsync<long?>(new CommandDefinition("SELECT Id FROM Products WHERE Market='TW' AND Symbol='00631L'",transaction:tx,cancellationToken:ct));
        if(productId is null){await tx.CommitAsync(ct);return;}
        var inserted=await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO SeedVersions(Version) VALUES('market-references-00631L-v1')",transaction:tx,cancellationToken:ct));
        if(inserted!=0)
        {
            foreach(var row in new[] {new { Symbol="^TSE50",Name="FTSE TWSE Taiwan 50 Index",Type="UNDERLYING"},new {Symbol="^TWII",Name="TAIEX",Type="BROAD_MARKET"}})
            {
                await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO MarketReferenceInstruments(UserId,Symbol,Name,Market) VALUES(1,@Symbol,@Name,'TW')",row,tx,cancellationToken:ct));
                await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO ProductMarketReferences(UserId,ProductId,ReferenceInstrumentId,ReferenceType) SELECT 1,@productId,Id,@Type FROM MarketReferenceInstruments WHERE UserId=1 AND Symbol=@Symbol",new {productId,row.Symbol,row.Type},tx,cancellationToken:ct));
            }
        }
        await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<ReferenceInstrument>> GetInstrumentsAsync(long userId,CancellationToken ct)
    { await using var db=Connection();return (await db.QueryAsync<ReferenceInstrument>(Cmd("SELECT Id,UserId,Symbol,Name,Market FROM MarketReferenceInstruments WHERE UserId=@userId ORDER BY Id",new {userId},ct))).AsList(); }
    public async Task<ReferenceInstrument> SaveInstrumentAsync(long userId,long? id,string symbol,string name,string market,CancellationToken ct)
    {
        MarketReferenceValidation.Instrument(symbol,name,market);await using var db=Connection();
        try
        {
            if(id is null) id=await db.ExecuteScalarAsync<long>(Cmd("INSERT INTO MarketReferenceInstruments(UserId,Symbol,Name,Market) VALUES(@userId,@symbol,@name,@market); SELECT LAST_INSERT_ID();",new {userId,symbol,name,market},ct));
            else if(await db.ExecuteAsync(Cmd("UPDATE MarketReferenceInstruments SET Symbol=@symbol,Name=@name,Market=@market WHERE UserId=@userId AND Id=@id",new {userId,id,symbol,name,market},ct))==0) throw new KeyNotFoundException("Reference instrument not found.");
        }
        catch(MySqlException ex) when(ex.Number==1062){throw new InvalidOperationException("Reference instrument already exists.");}
        return new(id.Value,userId,symbol,name,market);
    }
    public async Task DeleteInstrumentAsync(long userId,long id,CancellationToken ct)
    {
        await using var db=Connection();try
        {if(await db.ExecuteAsync(Cmd("DELETE FROM MarketReferenceInstruments WHERE UserId=@userId AND Id=@id",new {userId,id},ct))==0) throw new KeyNotFoundException("Reference instrument not found.");}
        catch(MySqlException ex) when(ex.Number==1451){throw new InvalidOperationException("Remove product mappings before deleting this instrument.");}
    }
    public async Task<IReadOnlyList<ProductMarketReference>> GetReferencesAsync(long userId,long productId,CancellationToken ct)
    {
        await using var db=Connection();var rows=await db.QueryAsync<Row>(Cmd("SELECT r.Id,r.UserId,r.ProductId,r.ReferenceType,i.Id AS InstrumentId,i.Symbol,i.Name,i.Market FROM ProductMarketReferences r JOIN MarketReferenceInstruments i ON i.Id=r.ReferenceInstrumentId AND i.UserId=r.UserId WHERE r.UserId=@userId AND r.ProductId=@productId ORDER BY r.Id",new {userId,productId},ct));
        return rows.Select(x=>new ProductMarketReference(x.Id,x.UserId,x.ProductId,x.ReferenceType,new(x.InstrumentId,x.UserId,x.Symbol,x.Name,x.Market))).ToArray();
    }
    public async Task<ProductMarketReference> SaveReferenceAsync(long userId,long productId,long? id,long instrumentId,string referenceType,CancellationToken ct)
    {
        MarketReferenceValidation.Type(referenceType);await using var db=Connection();
        var instrument=await db.QuerySingleOrDefaultAsync<ReferenceInstrument>(Cmd("SELECT Id,UserId,Symbol,Name,Market FROM MarketReferenceInstruments WHERE UserId=@userId AND Id=@instrumentId",new {userId,instrumentId},ct)) ?? throw new KeyNotFoundException("Reference instrument not found.");
        try
        {
            if(id is null) id=await db.ExecuteScalarAsync<long>(Cmd("INSERT INTO ProductMarketReferences(UserId,ProductId,ReferenceInstrumentId,ReferenceType) VALUES(@userId,@productId,@instrumentId,@referenceType);SELECT LAST_INSERT_ID();",new {userId,productId,instrumentId,referenceType},ct));
            else if(await db.ExecuteAsync(Cmd("UPDATE ProductMarketReferences SET ReferenceInstrumentId=@instrumentId,ReferenceType=@referenceType WHERE UserId=@userId AND ProductId=@productId AND Id=@id",new {userId,productId,instrumentId,referenceType,id},ct))==0) throw new KeyNotFoundException("Product reference not found.");
        }
        catch(MySqlException ex) when(ex.Number==1062){throw new InvalidOperationException("Product reference already exists.");}
        return new(id.Value,userId,productId,referenceType,instrument);
    }
    public async Task DeleteReferenceAsync(long userId,long productId,long id,CancellationToken ct)
    {await using var db=Connection();if(await db.ExecuteAsync(Cmd("DELETE FROM ProductMarketReferences WHERE UserId=@userId AND ProductId=@productId AND Id=@id",new {userId,productId,id},ct))==0) throw new KeyNotFoundException("Product reference not found.");}
    private sealed class Row
    {
        public long Id{get;set;}public long UserId{get;set;}public long ProductId{get;set;}public string ReferenceType{get;set;}="";
        public long InstrumentId{get;set;}public string Symbol{get;set;}="";public string Name{get;set;}="";public string Market{get;set;}="";
    }
}
