var gulp = require('gulp');
var async = require('async');
var request = require('request');
var rimraf = require('rimraf');
var fs = require('fs');
var spawn = require('child_process').spawn;
var ncp = require('ncp').ncp;
var replace = require('replace-in-file');

gulp.task('mkdir-sdks', (callback) => {
  fs.mkdir('sdks/', (err) => {
    fs.mkdir('Assets/HiveMP/', (err) => {
      fs.mkdir('Assets/HiveMP/GeneratedSDKs/', (err) => {
        callback()
      });
    });
  });
});

gulp.task('generate-sdks', ['mkdir-sdks'], (callback) => {
  var apis = [
    {
      name: 'admin-session-api',
      url: 'https://admin-session-api.hivemp.com/api-docs',
    },
    {
      name: 'attribute-api',
      url: 'https://attribute-api.hivemp.com/api-docs',
    },
    {
      name: 'audit-api',
      url: 'https://audit-api.hivemp.com/api-docs',
    },
    {
      name: 'error-api',
      url: 'https://error-api.hivemp.com/api-docs',
    },
    {
      name: 'lobby-api',
      url: 'https://lobby-api.hivemp.com/api-docs',
    },
    /*{
      name: 'matchmaking-api',
      url: 'https://matchmaking-api.hivemp.com/api-docs',
    },*/
    {
      name: 'nat-punchthrough-api',
      url: 'https://nat-punchthrough-api.hivemp.com/api-docs',
    },
    {
      name: 'temp-session-api',
      url: 'https://temp-session-api.hivemp.com/api-docs',
    }
  ];
  async.map(
    apis,
    (item, callback) => {
      request(item, (err, response, body) => {
        if (err != null) {
          callback(err, null);
          return;
        }

        if (response.statusCode != 200) {
          callback(new Error('Got status code ' + response.statusCode), null);
          return;
        }

        var result = JSON.parse(body);

        var sdkName = result["x-sdk-csharp-package-name"];
        if (sdkName == undefined) {
          callback(new Error('Missing C# SDK name on ' + item.name), null);
          return;
        }

        rimraf('sdks/'+item.name, (err) => {
          var process = spawn(
            'java',
            [
              '-jar',
              'swagger-codegen-cli.jar',
              'generate',
              '-i',
              item.url,
              '-l',
              'CsharpDotNet2',
              '-o',
              ('sdks/'+item.name),
              '--additional-properties',
              'packageName=' + sdkName
            ]
          );
          process.on('exit', (code) => {
            if (code != 0) { 
              console.log('failed to generate', item.name);
              callback(new Error('sdk generation exited with non-zero exit code'));
            } else {
              console.log('generated', item.name);
              var didCallback = false;
              ncp(
                'sdks/' + item.name + '/src/main/CsharpDotNet2/HiveMP',
                'Assets/HiveMP/GeneratedSDKs',
                (err) => {
                  if (didCallback) {
                    return;
                  }

                  didCallback = true;

                  if (err) {
                    callback(err);
                    return;
                  }

                  callback(null, JSON.parse(body));
                });
            }
          });
        });
      });
    },
    (err, results) => {
      if (err != null) {
        callback(err, null);
        return;
      }

      callback();
    }
  );
});

gulp.task('patch-files', ['generate-sdks'], (ccb) => {
  replace({
    files: [
      'Assets/HiveMP/GeneratedSDKs/**/*.cs'
    ],
    replace: /using System\.Web\;/g,
    with: '',
  }, (err, changedFiles) => {
    if (err != null) {
      ccb(err);
      return;
    }

    replace({
      files: [
        'Assets/HiveMP/GeneratedSDKs/**/*.cs'
      ],
      replace: /ByteArray/g,
      with: 'byte[]',
    }, (err, changedFiles) => {
      if (err != null) {
        ccb(err);
        return;
      }

      replace({
        files: [
          'Assets/HiveMP/GeneratedSDKs/**/*.cs'
        ],
        replace: 'public ApiException(int errorCode, string message) : base(message)',
        with: 'public ApiException(int errorCode) : base("")',
      }, (err, changedFiles) => {
        if (err != null) {
          ccb(err);
          return;
        }

        replace({
          files: [
            'Assets/HiveMP/GeneratedSDKs/**/*.cs'
          ],
          replace: /public FileParameter ParameterToFile\(string name, Stream stream\)\s+{([^\}]+)}/g,
          with: '',
        }, (err, changedFiles) => {
          if (err != null) {
            ccb(err);
            return;
          }

          replace({
            files: [
              'Assets/HiveMP/GeneratedSDKs/**/*.cs'
            ],
            replace: /foreach\(var param in queryParams\)\s+request.AddParameter\(param.Key, param.Value, ParameterType.GetOrPost\);/g,
            with: 'foreach(var param in queryParams) request.AddParameter(param.Key, param.Value, ParameterType.QueryString);',
          }, (err, changedFiles) => {
            if (err != null) {
              ccb(err);
              return;
            }

            replace({
              files: [
                'Assets/HiveMP/GeneratedSDKs/**/*.cs'
              ],
              replace: /return \(Object\)RestClient.Execute\(request\);/g,
              with: `var complete = false;
            object response_ = null;
            var handle = RestClient.ExecuteAsync(request, (response, handle_) =>
            {
                complete = true;
                response_ = response;
            });
            while (!complete)
            {
                System.Threading.Thread.Sleep(0);
            }

            return response_;`,
            }, (err, changedFiles) => {
              if (err != null) {
                ccb(err);
                return;
              }

              ccb();
            });
          });
        });
      });
    });
  });
});

gulp.task('build-sdk', ['patch-files'], function(callback) {
  var process = spawn(
    'powershell.exe',
    [
      '.\\Build.ps1'
    ]
  );
  process.on('exit', (code) => {
    fs.readFile('log.txt', 'utf-8', function(err, data) {
      console.log(data);

      if (code == 0) {
        callback();
        return;
      }

      callback(new Error('Unexpected exit code: ' + code));
    });
  });
})

gulp.task('default', ['build-sdk']);