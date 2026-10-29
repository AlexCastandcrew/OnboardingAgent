# HTGPlus.Objects.ObjectCache

A comprehensive object caching and data access layer that provides transactional caching, object lifecycle management, and database operation utilities for entities implementing `IPrimaryKey`.

## Overview

The `ObjectCache` class serves as the central hub for:
- **Object Caching**: Efficient retrieval and storage of domain objects
- **Transaction Management**: Automatic cache synchronization with database transactions
- **Data Access**: Simplified database operations with built-in caching
- **Object Lifecycle**: Registration, storage, retrieval, and deletion of cached objects

## Key Features

### 🔄 Transactional Caching
Automatically manages cache state based on database transaction lifecycle, ensuring data consistency across commit and rollback operations.

### 📦 Object Retrieval
Multiple methods for fetching objects from cache or database:
- By primary key (ID)
- By external identifier
- By stored procedure execution
- By multiple primary keys

### ⚡ Asynchronous Operations
Full async/await support for non-blocking database operations and improved performance.

### 🎯 Type Safety
Generic methods ensure compile-time type safety for all cached objects.

## Core Methods

### Object Retrieval

#### Get by ID
```csharp
// Synchronous retrieval
T obj = ObjectCache.Get<MyEntity>(id);

// Asynchronous retrieval
MyEntity obj = await ObjectCache.GetAsync<MyEntity>(id);
```

#### Get by External ID
```csharp
// Retrieve using external identifier
T obj = ObjectCache.GetByExternalId<MyEntity>("external-123", "subType", "region");

// Async version
MyEntity obj = await ObjectCache.GetByExternalIdAsync<MyEntity>("external-123");
```

#### Get Multiple Objects
```csharp
// Get multiple objects by their IDs
IList<MyEntity> entities = ObjectCache.Get<MyEntity>(id1, id2, id3);

// Async version
IList<MyEntity> entities = await ObjectCache.GetAsync<MyEntity>(id1, id2, id3);
```

### Database Operations

#### Execute Commands
```csharp
// Execute and load objects
IList<MyEntity> results = ObjectCache.Execute<MyEntity>("SELECT * FROM MyTable");

// Execute with parameters
IList<MyEntity> results = ObjectCache.Execute<MyEntity>(
    "SELECT * FROM MyTable WHERE Status = @status", 
    new SqlParameter("@status", "Active")
);
```

#### Execute Non-Query Operations
```csharp
// Synchronous execution
ObjectCache.ExecuteNonQuery("UPDATE MyTable SET Status = @status", parameters);

// Asynchronous execution
await ObjectCache.ExecuteNonQueryAsync("DELETE FROM MyTable WHERE Id = @id", parameters);
```

### Object Lifecycle Management

#### Registration
```csharp
// Register object type for caching
ObjectCache.Register<MyEntity>();

// Register derived type
ObjectCache.Register<DerivedEntity, BaseEntity>();
```

#### Storage and Deletion
```csharp
// Store object in cache and database
MyEntity stored = ObjectCache.Store(myEntity);

// Delete object
ObjectCache.Delete(myEntity);

// Check if object is new
bool isNew = ObjectCache.IsNew(myEntity);
```

#### Event Handlers
```csharp
// Add store event handler
ObjectCache.AddOnStoreHandler<MyEntity>(entity => 
{
    Console.WriteLine($"Entity {entity.PrimaryKey} was stored");
});

// Add delete event handler
ObjectCache.AddOnDeleteHandler<MyEntity>(entity => 
{
    Console.WriteLine($"Entity {entity.PrimaryKey} was deleted");
});
```

### Advanced Operations

#### Data Export
```csharp
// Export to JSON
JArray jsonData = await ObjectCache.ToJSONAsync("SELECT * FROM MyTable", CommandType.Text);

// Export with headers
DataWithHeaders data = await ObjectCache.ToDataWithHeadersAsync("SELECT * FROM MyTable", CommandType.Text);
```

#### Object Search
```csharp
// Find object using predicate
MyEntity found = ObjectCache.Find<MyEntity>(e => e.Name == "SearchValue");
```

#### Cache Management
```csharp
// Commit all pending changes
ObjectCache.Commit();

// Attach existing object to cache
MyEntity attached = ObjectCache.Attach(existingEntity);
```

## Requirements

- Objects must implement `IPrimaryKey` interface
- Designed for use with SQL Server databases
- Requires transactional context for optimal performance

## Usage Example

```csharp
// Register the entity type
ObjectCache.Register<Customer>();

// Retrieve a customer
Customer customer = await ObjectCache.GetAsync<Customer>(customerId);

if (customer == null)
{
    // Create new customer
    customer = new Customer { Name = "John Doe", Email = "john@example.com" };
}
else
{
    // Update existing customer
    customer.Email = "newemail@example.com";
}

// Store the customer (insert or update)
ObjectCache.Store(customer);

// Commit changes
ObjectCache.Commit();
```

## Thread Safety

The `ObjectCache` class is designed to work within transactional boundaries and should be used appropriately within your application's transaction management strategy.