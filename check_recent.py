import psycopg2
conn = psycopg2.connect('host=aws-1-eu-west-1.pooler.supabase.com port=6543 dbname=postgres user=postgres.hnyflybpvbpinrhdqimp password=Qas1#jdhkda')
cur = conn.cursor()
cur.execute('SELECT "Id", "Status", "CreatedAt" FROM "BatchSessions" ORDER BY "CreatedAt" DESC LIMIT 5')
for row in cur.fetchall():
    print(row)
conn.close()
