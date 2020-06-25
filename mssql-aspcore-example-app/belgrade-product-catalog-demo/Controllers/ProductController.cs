﻿using Belgrade.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace ProductCatalog.Controllers
{
    [Route("api/[controller]")]
    public class ProductController : Controller
    {
        IQueryPipe sqlQuery = null;
        ICommand sqlCmd = null;
        private readonly string EMPTY_PRODUCTS_ARRAY = "{\"data\":[]}";
        private readonly byte[] EMPTY_PRODUCTS_ARRAY_GZIPPED = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0xAB, 0x66, 0x50, 0x62, 0x48, 0x61, 0x48, 0x64, 0x28, 0x01, 0x62, 0x25, 0x06, 0x2B, 0x86, 0x68, 0x86, 0x58, 0x86, 0x5A, 0x06, 0x00, 0xB3, 0x4C, 0x62, 0xB2, 0x16, 0x00, 0x00, 0x00 };
        private readonly ILogger logger;

        public ProductController(IQueryPipe sqlQueryService, ICommand sqlCommandService, ILogger<ProductController> logger)
        {
            this.sqlQuery = sqlQueryService;
            this.sqlCmd = sqlCommandService;
            this.logger = logger;
        }

        // GET api/Product
        public async Task Get()
        {
            await sqlQuery
                .OnError(
                    ex =>
                    {
                        logger.LogError("Error while trying to get products: {Error}\n{StackTrace}", ex.Message, ex.StackTrace);
                        this.Response.StatusCode = 500;
                        throw ex;
                    })
                .Stream(@"
            select  ProductID, Name, Color, Price, Quantity,
                    JSON_VALUE(Data, '$.MadeIn') as MadeIn, JSON_QUERY(Tags) as Tags
            from Product
            FOR JSON PATH, ROOT('data')", Response.Body, EMPTY_PRODUCTS_ARRAY);
        }

        // GET api/Product/compressed
        [HttpGet("compressed")]
        public async Task GetCompressed()
        {
            Response.Headers.Add("Content-Type", "application/json;charset=utf-16");
            Response.Headers.Add("Content-Encoding", "gzip");
            await sqlQuery.Stream(@"
select COMPRESS(
           (select  ProductID, Name, Color, Price, Quantity,
                    JSON_VALUE(Data, '$.MadeIn') as MadeIn, JSON_QUERY(Tags) as Tags
            from Product
            FOR JSON PATH, ROOT('data') )
)", Response.Body, EMPTY_PRODUCTS_ARRAY_GZIPPED);
        }

        // GET api/Product/5
        [HttpGet("{id}")]
        public async Task Get(int id)
        {
            var cmd = new SqlCommand(
@"select    ProductID, Product.Name, Color, Price, Quantity,
            Company.Name as Company, Company.Address, Company.Email, Company.Phone 
from Product
    join Company on Product.CompanyID = Company.CompanyID
where ProductID = @id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER");

            cmd.Parameters.AddWithValue("id", id);
            await sqlQuery.Stream(cmd, Response.Body, "{}");
        }

        // POST api/Product
        [HttpPost]
        public async Task Post()
        {
            string product = new StreamReader(Request.Body).ReadToEnd();
            var cmd = new SqlCommand("EXEC InsertProductFromJson @ProductJson");
            cmd.Parameters.AddWithValue("ProductJson", product);
            await sqlCmd.ExecuteNonQuery(cmd);
        }

        // PUT api/Product/5
        [HttpPut("{id}")]
        public async Task Put(int id)
        {
            string product = new StreamReader(Request.Body).ReadToEnd();
            var cmd = new SqlCommand("EXEC UpdateProductFromJson @ProductID, @ProductJson");
            cmd.Parameters.AddWithValue("ProductID", id);
            cmd.Parameters.AddWithValue("ProductJson", product);
            await sqlCmd.ExecuteNonQuery(cmd);
        }

        // DELETE api/Product/5
        [HttpDelete("{id}")]
        public async Task Delete(int id)
        {
            var cmd = new SqlCommand(@"delete Product where ProductID = @id");
            cmd.Parameters.AddWithValue("id", id);
            await sqlCmd.ExecuteNonQuery(cmd);
        }

        [HttpGet("temporal")]
        [Produces("application/json")]
        public async Task Get(DateTime? date)
        {
            if (date == null)
                await this.sqlQuery.Stream("EXEC GetProducts", this.Response.Body, EMPTY_PRODUCTS_ARRAY);
            else
            {
                var cmd = new SqlCommand("EXEC GetProductsAsOf @date");
                cmd.Parameters.AddWithValue("@date", date);
                await this.sqlQuery.Stream(cmd, this.Response.Body, EMPTY_PRODUCTS_ARRAY);
            }
        }

        [HttpGet("restore")]
        [Produces("application/json")]
        public void RestoreVersion(int ProductId, DateTime DateModified)
        {
            var cmd = new SqlCommand("EXEC RestoreProduct @productid, @date");
            cmd.Parameters.AddWithValue("@productid", ProductId);
            cmd.Parameters.AddWithValue("@date", DateModified);
            this.sqlCmd
                .OnError(
                    ex =>
                    {
                        logger.LogError("Error while trying to restore product with id {ProductID} from time {DateModified}.\n{Error}\n{StackTrace}", ProductId, DateModified, ex.Message, ex.StackTrace);
                        this.Response.StatusCode = 500;
                        throw ex;
                    })
                .ExecuteNonQuery(cmd);
        }


        [HttpGet("Report1")]
        [Produces("application/json")]
        public async Task Report1()
        {
            await sqlQuery
                .Stream(@"
select color as x, sum(quantity) as y
from product
where color is not null
group by color
for json path", Response.Body, EMPTY_PRODUCTS_ARRAY);
        }


        [HttpGet("Report2")]
        [Produces("application/json")]
        public async Task Report2()
        {
            await sqlQuery
                .Stream(@"
select name as [key], [values].x, [values].y
	from company
		join (select companyid, color as x, sum(quantity) as y
				from product
				where color is not null
				group by companyid, color
				) as [values] on company.companyid = [values].companyid
order by company.companyid
for json auto", Response.Body, EMPTY_PRODUCTS_ARRAY);
        }
    }
}