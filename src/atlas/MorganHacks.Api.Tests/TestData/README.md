`GeoIP2-Country-Test.mmdb` is MaxMind's test database from
https://github.com/maxmind/MaxMind-DB/tree/main/test-data, distributed under the
included MIT license. It is used only by tests and is not included in the API
image. Production uses the DB-IP country database downloaded during the image
build, with the attribution shown in the geography report.
