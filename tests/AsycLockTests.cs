using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LndHub;
using Xunit;
using Xunit.Abstractions;

public class AsycLockTests
{
    public class AsyncDuplicateLockTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public AsyncDuplicateLockTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        public class Wallet
        {
            private static AsyncDuplicateLock _lock = new();

            public string Id { get; set; }
            public decimal Balance { get; private set; }

            public Wallet(string id, decimal initialBalance)
            {
                Id = id;
                Balance = initialBalance;
            }

            public async Task<bool> Spend(decimal amount)
            {
                using (await _lock.LockAsync(Id))
                {
                    if (Balance >= amount)
                    {
                        Balance -= amount;
                        return true;
                    }

                    return false;
                }
            }
        }

        [Fact]
        public async Task Spend_WhenConcurrentlyExceedingBalance_ShouldPreventOverdraw()
        {
            // Setup
            var wallets = new List<Wallet>()
            {
                new Wallet("WalletA", 100), // A wallet with a balance of 100,
                new Wallet("WalletB", 100), // A wallet with a balance of 100,
                new Wallet("WalletC", 100), // A wallet with a balance of 100,
                new Wallet("WalletD", 100) // A wallet with a balance of 100
            };
            int numberOfOperations = 50; // Number of concurrent spend attempts
            decimal spendAmount = 5; // Each attempt tries to spend 5

            // Act
            var tasks = new List<Task>();
            foreach (var wallet in wallets)
            {
                for (int i = 0; i < numberOfOperations; i++)
                {
                    tasks.Add(wallet.Spend(spendAmount));
                }
            }


            await Task.WhenAll(tasks);

            foreach (var wallet in wallets)
            {
                // The total spend attempts (50 * 5 = 250) exceed the initial balance (100),
                // so the final balance should be non-negative and less than the total attempted spend.
                Assert.True(wallet.Balance >= 0, $"Wallet {wallet.Id} overdrawn with balance: {wallet.Balance}");
                Assert.True(wallet.Balance < numberOfOperations * spendAmount,
                    $"Wallet {wallet.Id} balance should be less than total attempted spend.");
            }
        }

        [Fact]
        public async Task LockAsync_MultipleParallelForeach_ShouldNotDuplicateEntries()
        {
            var lockObj = new AsyncDuplicateLock();
            var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var resultList = new ConcurrentDictionary<string, int>();
            int iterationsPerLetter = 100; // Number of iterations per letter

            async Task WriteToList(char letter)
            {
                while (true)

                {
                    var release = await lockObj.LockOrBustAsync(letter);
                    if (release is null)
                    {
                        continue;
                    }

                    using (release)
                    {
                        try
                        {
                            if (resultList.TryGetValue(letter.ToString(), out var count) &&
                                count >= iterationsPerLetter)
                            {
                                break;
                            }

                            resultList.AddOrUpdate(letter.ToString(), 1, (key, oldValue) => oldValue + 1);
                            _outputHelper.WriteLine($"write{letter}{count}");
                        }
                        catch (Exception e)
                        {
                            // _outputHelper.WriteLine(e + letter.ToString());
                        }
                    }
                }
            }


            await Task.WhenAll(
                Parallel.ForEachAsync(alphabet, async (letter, token) => await WriteToList(letter)),
                Parallel.ForEachAsync(alphabet, async (letter, token) => await WriteToList(letter)),
                Parallel.ForEachAsync(alphabet, async (letter, token) => await WriteToList(letter)));

            var missing = new List<string>();
            // Assert
            foreach (var letter in alphabet)
            {
                int count = resultList[letter.ToString()];
                if (count != iterationsPerLetter)
                {
                    missing.Add($"{letter}- {count}");
                }
            }

            _outputHelper.WriteLine($"Missing: {missing.Count}");
            Assert.Empty(missing);
        }
    }
}
